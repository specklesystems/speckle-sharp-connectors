using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using DataObject = Speckle.Objects.Data.DataObject; // disambiguate from System.Windows.Forms.DataObject (ETABS uses WinForms)
using Path = System.IO.Path;

namespace Speckle.Connectors.CSiShared.Builders;

/// <summary>
/// Speckle 4.0 send path for CSi/ETABS: instead of assembling a <see cref="Speckle.Sdk.Models.Collections.Collection"/>
/// graph of <see cref="DataObject"/>s and serializing it, this converts each element and drives the SDK
/// <see cref="ObjectsArtifactPipeline"/> to write the client-side artefact triple directly — <c>geometries.parquet</c>
/// (SGEO Point/Line/Mesh blobs), <c>eav.*.parquet</c> (properties), <c>envelope.*.parquet</c> (the level→category
/// collection topology) — then uploads via <see cref="ArtifactPipeline"/>.
/// </summary>
/// <remarks>
/// <para>Registering this builder in <c>AddCsi</c> is the only switch — <c>SendOperation</c> auto-selects
/// <c>SendViaArtifacts</c>; no binding/UX change.</para>
/// <para>CSi elements have no lossless raw encoding — the display geometry (Point/Line/Mesh from
/// <c>DisplayValueExtractor</c>) IS the geometry, and it's SGEO-encodable, so this only emits <c>DISPLAY</c> geometry.
/// Grouping reuses <see cref="CsiSendCollectionManager.GetCollectionSegments"/> (base: by type; ETABS: level→category)
/// as nested CONTAINER nodes + IN_COLLECTION. Analysis results flatten into <c>structural_results</c> rows (all 8
/// CSi result types, three identity shapes — see <c>s_resultDescriptors</c>) and the model's database unit set rides
/// <c>eav.model</c> as <c>units.*</c> rows, since result rows themselves are unitless. **Deferred this pass:**
/// section/material <c>GroupProxy</c>s (CSi has no render materials/colors).</para>
/// <para><b>Threading.</b> Two-phase like Rhino: the CSi COM <c>SapModel</c> API is main-thread-affine, so phase 1
/// (<see cref="CollectOnMain"/>) converts on the host thread → a pure-Speckle snapshot; phase 2
/// (<see cref="WriteBundle"/>) builds the parquet bundle on a worker (the pipeline's sync-over-async IO deadlocks on a
/// UI SynchronizationContext).</para>
/// </remarks>
public class CsiArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<CsiConversionSettings> converterSettings,
  CsiSendCollectionManager collectionManager,
  AnalysisResultsExtractor analysisResultsExtractor,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ISpeckleApplication speckleApplication,
  ILogger<CsiArtifactRootObjectBuilder> logger
) : IArtifactRootObjectBuilder<ICsiWrapper>
{
  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<ICsiWrapper> objects,
    string projectId,
    string ingestionId,
    string versionId,
    Account account,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var outputDir = Path.Combine(Path.GetTempPath(), "Speckle", "artifacts", versionId);
    System.IO.Directory.CreateDirectory(outputDir);

    using var session = ArtefactSessionLog.Start("CSi", ArtefactDirection.Send, projectId, null, versionId, logger);

    // Phase 1 — convert on the host thread (CSi COM API is main-thread-affine) → pure-Speckle snapshot.
    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    // Phase 2 — build + upload on a worker thread (no UI SynchronizationContext → parquet IO can't deadlock).
    return await threadContext.RunOnWorkerAsync(async () =>
    {
      BundleResult built;
      using (session.Phase("Write"))
      {
        built = WriteBundle(collected, session, versionId, outputDir, onOperationProgressed, cancellationToken);
      }

      using var pipeline = artifactPipelineFactory.CreateInstance(
        projectId,
        ingestionId,
        versionId,
        account,
        outputDir,
        cancellationToken
      );

      onOperationProgressed.Report(new("Uploading...", null));
      string finalVersionId;
      using (session.Phase("Upload"))
      {
        finalVersionId = await pipeline
          .UploadFilesAsync(built.Bundle, built.RootId, built.ObjectCount)
          .ConfigureAwait(false);
      }

      return new ArtifactBuildResult(finalVersionId, built.RootId, built.Results);
    });
  }

  // ── Phase 1 (host thread): CSi API → pure-Speckle snapshot ────────────────────────────────────────────
  private CollectedModel CollectOnMain(
    IReadOnlyList<ICsiWrapper> objects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var units = converterSettings.Current.SpeckleUnits;
    // Database units are what the result magnitudes are expressed in — captured here (COM is main-thread-affine).
    var (forceUnits, temperatureUnits) = CsiDatabaseUnits.GetForceAndTemperature(converterSettings.Current.SapModel);
    var collected = new List<CollectedObject>(objects.Count);
    var results = new List<SendConversionResult>(objects.Count);
    // CSi analysis results key back to objects by the element NAME; interned objects key by applicationId — so map.
    var nameToAppId = new Dictionary<string, string>(StringComparer.Ordinal);

    int count = 0;
    foreach (ICsiWrapper wrapper in objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string sourceType = wrapper.ObjectName;
      var sw = Stopwatch.StartNew();
      try
      {
        Base converted = converter.Convert(wrapper);
        var segments = collectionManager.GetCollectionSegments(converted);
        string appId = converted.applicationId ?? Guid.NewGuid().ToString();
        nameToAppId[wrapper.Name] = appId;
        collected.Add(new CollectedObject(appId, sourceType, converted, segments));
        results.Add(new(Status.SUCCESS, appId, sourceType, converted));
        session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (NotImplementedException ex)
      {
        // Expected for some element types (SAP2000 cable/solid, ETABS link/tendon) — a warning, not a failure.
        results.Add(new(Status.WARNING, wrapper.Name, sourceType, null, ex));
        session.RecordObject(wrapper.Name, sourceType, Status.WARNING, ex.Message, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert {SourceType}", sourceType);
        results.Add(new(Status.ERROR, wrapper.Name, sourceType, null, ex));
        session.RecordObject(wrapper.Name, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
      }

      onOperationProgressed.Report(new("Converting", (double)++count / objects.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var resultRows = ExtractResultRows(objects, nameToAppId, session);
    return new CollectedModel(units, forceUnits, temperatureUnits, collected, resultRows, results, nameToAppId);
  }

  // Runs the (gated) analysis-results extraction on the host thread and flattens the extractor's nested dicts into
  // structural_results rows — all 8 CSi result types, across the schema's three identity shapes: object-level
  // (frame/joint → object_index), group-level (pier/spandrel → element_name + story), model/story-level (base
  // reactions, modal periods, story drifts/forces → location/step). A results failure (model unlocked / analysis
  // not run) is logged and skipped so the geometry+properties send still succeeds.
  private List<StructuralResultRow> ExtractResultRows(
    IReadOnlyList<ICsiWrapper> objects,
    Dictionary<string, string> nameToAppId,
    ArtefactSessionLog session
  )
  {
    var rows = new List<StructuralResultRow>();
    var cases = converterSettings.Current.SelectedLoadCasesAndCombinations;
    var resultTypes = converterSettings.Current.SelectedResultTypes;
    if (cases is not { Count: > 0 } || resultTypes is not { Count: > 0 })
    {
      // Diagnostic: this used to return silently, so an empty results file looked like a bug with no signal.
      // Surface the two selection counts (both must be > 0) in the log AND the conversion report so a missing
      // model-card selection (or settings not reaching converterSettings.Current) is visible, not silent.
      logger.LogWarning(
        "Structural results NOT extracted: SelectedLoadCasesAndCombinations={CaseCount}, SelectedResultTypes={ResultTypeCount} — both must be non-empty. Check the model card's Load Cases & Result Types settings.",
        cases?.Count ?? 0,
        resultTypes?.Count ?? 0
      );
      session.RecordObject(
        "analysis-results",
        "AnalysisResults",
        Status.WARNING,
        $"results not extracted — selected load cases: {cases?.Count ?? 0}, selected result types: {resultTypes?.Count ?? 0} (both must be > 0)",
        0
      );
      return rows;
    }

    try
    {
      using (session.Phase("Analysis results"))
      {
        var summary = BuildObjectSummary(objects);
        Base analysisResults = analysisResultsExtractor.ExtractAnalysisResults(
          cases.ToList(),
          resultTypes.ToList(),
          summary
        );

        foreach (var descriptor in s_resultDescriptors)
        {
          if (analysisResults[descriptor.ResultsKey] is IDictionary<string, object> node)
          {
            FlattenResultType(node, descriptor, nameToAppId, rows);
          }
        }
        session.SetStat("resultRows", rows.Count);

        // Diagnostic: selections WERE present (we passed the gate) but the extractor produced nothing. Surface it —
        // usually the model isn't locked/analysed, or the selected cases have no computed results.
        if (rows.Count == 0)
        {
          logger.LogWarning(
            "Structural results extraction ran for {Cases} case(s) x {Types} result type(s) but produced 0 rows — is the model locked (analysis run) and do the selected cases have results?",
            cases.Count,
            resultTypes.Count
          );
          session.RecordObject(
            "analysis-results",
            "AnalysisResults",
            Status.WARNING,
            $"extraction produced 0 rows for {cases.Count} case(s) / {resultTypes.Count} result type(s) — check the model is locked + analysed and the cases have results",
            0
          );
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Analysis result extraction skipped");
      session.RecordObject("analysis-results", "AnalysisResults", Status.WARNING, ex.Message, 0);
    }
    return rows;
  }

  private static Dictionary<ModelObjectType, List<string>> BuildObjectSummary(IReadOnlyList<ICsiWrapper> objects)
  {
    var summary = new Dictionary<ModelObjectType, List<string>>();
    foreach (var wrapper in objects)
    {
      if (!summary.TryGetValue(wrapper.ObjectType, out var list))
      {
        list = new List<string>();
        summary[wrapper.ObjectType] = list;
      }
      list.Add(wrapper.Name);
    }
    return summary;
  }

  // Walks one result type's nested dict (per its GroupingKeys) and emits one row per leaf component value.
  private static void FlattenResultType(
    IDictionary<string, object> node,
    ResultDescriptor descriptor,
    Dictionary<string, string> nameToAppId,
    List<StructuralResultRow> rows
  )
  {
    Walk(
      node,
      descriptor.GroupingKeys,
      0,
      new Dictionary<string, string>(StringComparer.Ordinal),
      (axes, leaf) =>
      {
        string? Axis(string? key) => key != null && axes.TryGetValue(key, out var v) ? v : null;

        string? objectAppId = null;
        string? location = Axis(descriptor.LocationKey);
        if (descriptor.ElementKey != null && axes.TryGetValue(descriptor.ElementKey, out var elementName))
        {
          if (nameToAppId.TryGetValue(elementName, out var appId))
          {
            objectAppId = appId;
          }
          else
          {
            location = elementName; // analysis-only element not among the sent objects — keep its name
          }
        }

        axes.TryGetValue("LoadCase", out var loadCase);
        double? station =
          axes.TryGetValue("ElmSta", out var sta)
          && double.TryParse(sta, NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            ? s
            : null;
        int? step = null;
        if (
          axes.TryGetValue("StepNum", out var sn)
          && int.TryParse(sn, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si)
        )
        {
          step = si;
        }
        else if (
          axes.TryGetValue("Mode", out var mo)
          && int.TryParse(mo, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mi)
        )
        {
          step = mi;
        }

        if (descriptor.DriftPivot)
        {
          // The extractor leaves Direction/Drift/Label/X/Y/Z as sibling leaf values; the spec keeps Drift per
          // Direction and drops the rest (locked decision, speckle-bundle-spec structural-results rationale).
          rows.Add(
            new StructuralResultRow(
              null,
              location,
              descriptor.ResultType,
              loadCase ?? "",
              "drift",
              null,
              step,
              (double)leaf["Drift"],
              null,
              (string)leaf["Direction"]
            )
          );
          return;
        }

        foreach (var kv in leaf)
        {
          // The CSi API types every result quantity as double[] — anything else here is ResultsConfiguration drift.
          var value = (double)kv.Value;
          var component =
            descriptor.ComponentRenames != null && descriptor.ComponentRenames.TryGetValue(kv.Key, out var renamed)
              ? renamed
              : kv.Key;
          rows.Add(
            new StructuralResultRow(
              objectAppId,
              location,
              descriptor.ResultType,
              loadCase ?? "",
              component,
              station,
              step,
              value,
              Axis(descriptor.ElementNameKey),
              Axis(descriptor.PositionKey)
            )
          );
        }
      }
    );
  }

  private static void Walk(
    object node,
    IReadOnlyList<string> groupingKeys,
    int level,
    Dictionary<string, string> axes,
    Action<Dictionary<string, string>, IDictionary<string, object>> onLeaf
  )
  {
    if (node is not IDictionary<string, object> dict)
    {
      return;
    }
    if (level >= groupingKeys.Count)
    {
      onLeaf(axes, dict);
      return;
    }

    var key = groupingKeys[level];
    if (key.StartsWith("Wrap:", StringComparison.Ordinal))
    {
      var actual = key["Wrap:".Length..];
      if (dict.TryGetValue(actual, out var wrappedObj) && wrappedObj is IDictionary<string, object> wrapped)
      {
        foreach (var entry in wrapped)
        {
          axes[actual] = entry.Key;
          Walk(entry.Value, groupingKeys, level + 1, axes, onLeaf);
        }
      }
    }
    else
    {
      foreach (var entry in dict)
      {
        axes[key] = entry.Key;
        Walk(entry.Value, groupingKeys, level + 1, axes, onLeaf);
      }
    }
  }

  // storyForces leaf keys are the ETABS database-table codes; the spec catalog names them semantically.
  private static readonly Dictionary<string, string> s_storyForceComponents = new(StringComparer.Ordinal)
  {
    ["P"] = "axial",
    ["VX"] = "majorShear",
    ["VY"] = "minorShear",
    ["T"] = "torsion",
    ["MX"] = "majorMoment",
    ["MY"] = "minorMoment",
  };

  // One entry per result type: the extractor's grouping axes, and which identity column each feeds.
  private static readonly ResultDescriptor[] s_resultDescriptors =
  {
    new(
      "frameForces",
      "frameForce",
      "Elm",
      null,
      null,
      null,
      new[] { "Elm", "LoadCase", "Wrap:ElmSta", "Wrap:StepNum" }
    ),
    new("jointReact", "jointReaction", "Elm", null, null, null, new[] { "Elm", "LoadCase", "Wrap:StepNum" }),
    new("baseReact", "baseReaction", null, null, null, null, new[] { "LoadCase", "Wrap:StepNum" }),
    new("modalPeriodsAndFrequencies", "modalPeriod", null, null, null, null, new[] { "LoadCase", "Wrap:Mode" }),
    new(
      "pierForces",
      "pierForce",
      null,
      "PierName",
      "StoryName",
      "Location",
      new[] { "PierName", "StoryName", "LoadCase", "Wrap:Location" }
    ),
    new(
      "spandrelForces",
      "spandrelForce",
      null,
      "SpandrelName",
      "StoryName",
      "Location",
      new[] { "SpandrelName", "StoryName", "LoadCase", "Wrap:Location" }
    ),
    // Direction is a leaf value, and the extractor keeps only the first row per (Story, LoadCase, StepNum) group —
    // so one direction per group survives, same as the v3 send path. Fixing that means promoting Direction to a
    // grouping key in CsiStoryDriftsResultsExtractor, which changes the legacy commit shape — deliberately deferred.
    new(
      "storyDrifts",
      "storyDrift",
      null,
      null,
      "Story",
      null,
      new[] { "Story", "LoadCase", "Wrap:StepNum" },
      DriftPivot: true
    ),
    new(
      "storyForces",
      "storyForce",
      null,
      null,
      "Story",
      "Location",
      new[] { "Story", "LoadCase", "Location" },
      s_storyForceComponents
    ),
  };

  private sealed record ResultDescriptor(
    string ResultsKey,
    string ResultType,
    string? ElementKey, // axis resolved via name→appId (object-level identity)
    string? ElementNameKey, // axis → element_name (group-level identity: pier/spandrel name)
    string? LocationKey, // axis → location (story)
    string? PositionKey, // axis → position_label (Top/Bottom)
    IReadOnlyList<string> GroupingKeys,
    IReadOnlyDictionary<string, string>? ComponentRenames = null,
    bool DriftPivot = false
  );

  private sealed record StructuralResultRow(
    string? ObjectAppId,
    string? Location,
    string ResultType,
    string LoadCase,
    string Component,
    double? Station,
    int? Step,
    double? Value,
    string? ElementName = null,
    string? PositionLabel = null
  );

  // ── Phase 2 (worker thread): snapshot → parquet bundle ────────────────────────────────────────────────
  [SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling")]
  private BundleResult WriteBundle(
    CollectedModel model,
    ArtefactSessionLog session,
    string versionId,
    string outputDir,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    ZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded (no-op on net8+)
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId, producer: speckleApplication);

    // Model-wide unit set → eav.model (same units.* vocabulary the migration harness writes): the
    // structural_results rows are unitless, so these are the only record of what the magnitudes mean.
    AddUnitModelProperty(pipeline, "units.distance", model.Units);
    AddUnitModelProperty(pipeline, "units.force", model.ForceUnits);
    AddUnitModelProperty(pipeline, "units.temperature", model.TemperatureUnits);

    var collectionKByPath = new Dictionary<string, int>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddCollection(pipeline, co.Segments, collectionKByPath);
      int objK = pipeline.InternObject(co.ApplicationId);
      pipeline.InCollection(objK, collK, 0);

      List<Base> display;
      Dictionary<string, object?>? properties;
      if (co.Converted is DataObject dataObject)
      {
        display = dataObject.displayValue;
        properties = dataObject.properties;
      }
      else
      {
        display = new List<Base> { co.Converted };
        properties = null;
      }

      pipeline.AddProperties(
        co.ApplicationId,
        properties ?? s_emptyProps,
        RootScalars(co.Converted.speckle_type, co.Converted["name"]?.ToString(), model.Units, co.SourceType)
      );

      int ord = 0;
      foreach (Base fragment in display)
      {
        try
        {
          string gAppId = fragment.applicationId ?? $"{co.ApplicationId}:g{ord}";
          int gK = pipeline.AddGeometry(gAppId, fragment);
          pipeline.Display(objK, gK, ord++);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          logger.LogWarning(
            ex,
            "Skipped unsupported display geometry {Type} on {AppId}",
            fragment.speckle_type,
            co.ApplicationId
          );
        }
      }

      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    // Analysis results → {v}.eav.structural_results.parquet (object-level rows join back via object_index;
    // group-level pier/spandrel rows via element_name; story-level rows via location).
    foreach (var r in model.ResultRows)
    {
      pipeline.AddStructuralResult(
        r.ObjectAppId,
        r.Location,
        r.ResultType,
        r.LoadCase,
        r.Component,
        r.Station,
        r.Step,
        r.Value,
        elementName: r.ElementName,
        positionLabel: r.PositionLabel
      );
    }

    EmitFrameJointConnectivity(pipeline, model);

    // Default scene view: the CSi collection tree (IN_COLLECTION); the CONTAINER parent chain carries the nesting.
    pipeline.AddSceneView(new SceneView(0, "Default", true, new[] { SceneViewKey.Rel(RelKind.InCollection) }));

    pipeline.Complete();

    var bundle = System
      .IO.Directory.EnumerateFiles(outputDir, versionId + ".*")
      .Where(p => p.EndsWith(".parquet", StringComparison.Ordinal))
      .ToDictionary(p => Path.GetFileName(p)!, p => p, StringComparer.Ordinal);

    var objectCount = model.Results.Count(r => r.Status == Status.SUCCESS);
    var rootId = $"binary-{versionId}";

    session.SetStat("files", bundle.Count);
    session.SetStat("objects", objectCount);
    session.SetStat("collections", collectionKByPath.Count);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  // Resolves (and interns once) the nested CONTAINER chain for the given collection segments (outermost → leaf).
  private static int GetOrAddCollection(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyList<string> segments,
    Dictionary<string, int> cache
  )
  {
    int? parentK = null;
    var soFar = "";
    foreach (var raw in segments)
    {
      var name = string.IsNullOrWhiteSpace(raw) ? "Unnamed" : raw;
      soFar = soFar.Length == 0 ? name : soFar + "/" + name;
      if (cache.TryGetValue(soFar, out var existing))
      {
        parentK = existing;
        continue;
      }
      int collK = pipeline.AddCollection(soFar, name, parentK, "Collection");
      cache[soFar] = collK;
      parentK = collK;
    }
    // segments is always non-empty (GetCollectionSegments returns at least one), so parentK is set.
    return parentK ?? pipeline.AddCollection("Model", "Model", null, "Collection");
  }

  private static readonly Dictionary<string, object?> s_emptyProps = new();

  // "NotApplicable" is a silently failed GetDatabaseUnits_2 call; "none" is Units.None — neither informs.
  private static void AddUnitModelProperty(ObjectsArtifactPipeline pipeline, string path, string? value)
  {
    if (!string.IsNullOrEmpty(value) && value != "NotApplicable" && value != "none")
    {
      pipeline.AddModelProperty(path, value);
    }
  }

  private static KeyValuePair<string, object?>[] RootScalars(
    string speckleType,
    string? name,
    string units,
    string sourceType
  ) =>
    new KeyValuePair<string, object?>[]
    {
      new("speckle_type", speckleType),
      new("name", name),
      new("units", units),
      new("type", sourceType),
    };

  // Member↔joint connectivity (CONNECTS_TO): a frame → its I-/J-end joint objects. The joint NAMES live in the
  // frame's "Geometry" properties (CsiFramePropertiesExtractor); NameToAppId maps a CSi element name → its
  // applicationId, and only sent objects are in that map, so an unsent joint is naturally skipped. This is the
  // slab↔beam↔column graph via shared joints — reverse-lookup a joint's object_index to find every member meeting
  // there.
  private static void EmitFrameJointConnectivity(ObjectsArtifactPipeline pipeline, CollectedModel model)
  {
    foreach (var co in model.Objects)
    {
      if (
        co.Converted is not Speckle.Objects.Data.DataObject dataObject
        || !dataObject.properties.TryGetValue("Geometry", out var g)
        || g is not IDictionary<string, object?> geometry
      )
      {
        continue;
      }
      int frameK = pipeline.InternObject(co.ApplicationId);
      foreach (var endKey in s_endJointKeys)
      {
        if (
          geometry.TryGetValue(endKey, out var jn)
          && jn is string jointName
          && jointName.Length > 0
          && model.NameToAppId.TryGetValue(jointName, out var jointAppId)
        )
        {
          pipeline.ConnectsTo(frameK, pipeline.InternObject(jointAppId));
        }
      }
    }
  }

  private static readonly string[] s_endJointKeys = { "I-End Joint", "J-End Joint" };

  private sealed record CollectedObject(
    string ApplicationId,
    string SourceType,
    Base Converted,
    IReadOnlyList<string> Segments
  );

  private sealed record CollectedModel(
    string Units,
    string ForceUnits,
    string TemperatureUnits,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyList<StructuralResultRow> ResultRows,
    IReadOnlyList<SendConversionResult> Results,
    IReadOnlyDictionary<string, string> NameToAppId
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
