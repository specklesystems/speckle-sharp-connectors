using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Converters.MicroStation.ToSpeckle;
using Speckle.Converters.MicroStation.ToSpeckle.Appearance;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Bundles;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Speckle 4.0 ("big-truck") send path for MicroStation: converts into the SDK's
/// <see cref="BundleBuilder"/>, which streams the client-side artefact bundle —
/// <c>geometries.parquet</c> (SGEO blobs), <c>eav.*.parquet</c> (properties),
/// <c>envelope.*.parquet</c> (relations + node topology). The SDK ships it
/// (<see cref="IBundleSender"/>). This is the SAME output format dgnextract produces, so the
/// envelope mapping ports 1:1:
/// <list type="bullet">
/// <item>DGN levels → flat CONTAINER("Layer") nodes + IN_COLLECTION (never LEVEL/ON_LEVEL — ENG-9131;
/// the layer containers stay top-level, matching the ENG-8965 def_ref rule)</item>
/// <item>reference occurrences → CONTAINER("Model") + IN_MODEL per attachment placement (ENG-8749)</item>
/// <item>shared cells → DEFINITION/INSTANCE + DEFINES via the instance unpacker; definition members
/// convert in their local frame</item>
/// <item>appearance rides the geometry plane: HAS_MATERIAL / HAS_COLOR per display fragment —
/// dgnextract's exact per-geometry channels, strictly separate</item>
/// <item>object rows are <c>Objects.Data.MicrostationObject</c> — the same speckle_type dgnextract stamps</item>
/// </list>
/// <para><b>Threading</b> (the Rhino split): collect runs on the MicroStation UI thread (the managed
/// DgnPlatform APIs are main-thread-affine), the parquet write on a worker — the artefact pipeline
/// does sync-over-async IO that deadlocks a blocked UI dispatcher.</para>
/// </summary>
public class MicroStationBundleBuilder(
  DisplayValueExtractor displayValueExtractor,
  PropertiesExtractor propertiesExtractor,
  GeometryMapper geometryMapper,
  IInstanceUnpacker<MicroStationRootObject> instanceUnpacker,
  IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
  IThreadContext threadContext,
  ISpeckleApplication speckleApplication,
  ILogger<MicroStationBundleBuilder> logger
) : IBundleBuilder<MicroStationRootObject>
{
  public async Task<BundleBuild> Build(
    IReadOnlyList<MicroStationRootObject> objects,
    string? projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var session = ArtefactSessionLog.Start("MicroStation", ArtefactDirection.Send, projectId, null, null, logger);

    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await threadContext.RunOnMainAsync(() =>
        Task.FromResult(CollectOnMain(objects, session, onOperationProgressed, cancellationToken))
      );
    }

    return await threadContext.RunOnWorkerAsync(() =>
    {
      using (session.Phase("Write"))
      {
        return Task.FromResult(WriteBundle(collected, onOperationProgressed, cancellationToken));
      }
    });
  }

  // ── Phase 1 (UI thread): DgnPlatform → pure-Speckle snapshot ─────────────────────────────

  private CollectedModel CollectOnMain(
    IReadOnlyList<MicroStationRootObject> rootObjects,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    string units = converterSettings.Current.SpeckleUnits;

    UnpackResult<MicroStationRootObject> unpack = instanceUnpacker.UnpackSelection(rootObjects);

    var collectedObjects = new List<CollectedObject>(unpack.AtomicObjects.Count);
    var results = new List<SendConversionResult>(unpack.AtomicObjects.Count);
    var occurrences = new Dictionary<string, string>(StringComparer.Ordinal); // tag → label
    var levels = new Dictionary<string, string>(StringComparer.Ordinal); // key → name

    int count = 0;
    foreach (MicroStationRootObject obj in unpack.AtomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string appId = obj.ApplicationId;
      string sourceType = SourceTypeOf(obj.Element);
      var sw = System.Diagnostics.Stopwatch.StartNew();
      try
      {
        if (obj.OccurrenceTag.Length > 0 && !occurrences.ContainsKey(obj.OccurrenceTag))
        {
          occurrences[obj.OccurrenceTag] = obj.ContainerLabel;
        }

        var (levelName, levelNumber) = PropertiesExtractor.GetLevelInfo(obj.Element);
        string? levelKey = null;
        if (!string.IsNullOrEmpty(levelName))
        {
          levelKey = $"{levelNumber}:{levelName}";
          levels[levelKey] = levelName!;
        }

        CollectedObject collected = CollectObject(obj, appId, sourceType, levelKey, units, unpack);
        collectedObjects.Add(collected);
        results.Add(new(Status.SUCCESS, appId, sourceType, collected.Proxy));
        session.RecordObject(appId, sourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert {SourceType}", sourceType);
        results.Add(new(Status.ERROR, appId, sourceType, null, ex));
        session.RecordObject(appId, sourceType, Status.ERROR, ex.Message, sw.ElapsedMilliseconds);
      }
      onOperationProgressed.Report(new("Converting", (double)++count / unpack.AtomicObjects.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return new CollectedModel(units, collectedObjects, levels, occurrences, unpack.InstanceDefinitionProxies, results);
  }

  private CollectedObject CollectObject(
    MicroStationRootObject obj,
    string appId,
    string sourceType,
    string? levelKey,
    string units,
    UnpackResult<MicroStationRootObject> unpack
  )
  {
    _ = units;
    if (unpack.InstanceProxies.TryGetValue(appId, out InstanceProxy? instanceProxy))
    {
      PropertiesResult instanceProps = propertiesExtractor.Extract(obj.Element);
      return new CollectedObject(
        appId,
        ResolveName(obj.Element) ?? sourceType,
        sourceType,
        levelKey,
        obj.OccurrenceTag,
        instanceProps.Properties,
        Proxy: instanceProxy,
        Geometry: null
      );
    }

    using IDisposable? occurrenceScope = obj.OccurrenceTransform is BG.DTransform3d t
      ? geometryMapper.PushTransform(t)
      : null;
    using IDisposable? definitionScope = unpack.AtomicDefinitionObjectIds.Contains(appId)
      ? geometryMapper.PushDefinitionFrame()
      : null;

    List<ExtractedGeometry> extracted = displayValueExtractor.Extract(obj.Element);
    for (int i = 0; i < extracted.Count; i++)
    {
      extracted[i].Geometry.applicationId ??= $"{appId}-g{i}";
    }

    PropertiesResult props = propertiesExtractor.Extract(obj.Element);
    if (props.IsCivil)
    {
      CivilQuantities.AddTo(extracted.Select(g => g.Geometry).ToList(), props.Properties);
    }

    return new CollectedObject(
      appId,
      ResolveName(obj.Element) ?? sourceType,
      sourceType,
      levelKey,
      obj.OccurrenceTag,
      props.Properties,
      Proxy: null,
      Geometry: extracted
    );
  }

  private static string? ResolveName(MgdElement element) =>
    element switch
    {
      MgdElements.SharedCellElement sc when !string.IsNullOrEmpty(sc.CellName) => sc.CellName,
      MgdElements.CellHeaderElement c when !string.IsNullOrEmpty(c.CellName) => c.CellName,
      _ => null,
    };

  private static string SourceTypeOf(MgdElement element)
  {
    string typeName = element.TypeName;
    return string.IsNullOrEmpty(typeName) ? element.ElementType.ToString() : typeName;
  }

  // ── Phase 2 (worker thread): pure-Speckle snapshot → BundleBuilder ───────────────────────

  private BundleBuild WriteBundle(
    CollectedModel model,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    ZstdNativeLoader.Ensure(logger); // net48: ensure the parquet Zstd native is loaded
    var bundle = new BundleBuilder(speckleApplication, model.Units);
    try
    {
      List<SendConversionResult> results = WriteInto(bundle, model, onOperationProgressed, cancellationToken);
      return new BundleBuild(bundle, results);
    }
    catch
    {
      bundle.Dispose();
      throw;
    }
  }

  private List<SendConversionResult> WriteInto(
    BundleBuilder bundle,
    CollectedModel model,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    const string SPECKLE_TYPE = "Objects.Data.MicrostationObject";

    // Definitions first so placements resolve to named definitions.
    var definitions = new Dictionary<string, BundleDefinition>(StringComparer.Ordinal);
    foreach (InstanceDefinitionProxy defProxy in model.Definitions)
    {
      string defId = defProxy.applicationId ?? throw new SpeckleException("definition proxy without id");
      definitions[defId] = bundle.GetOrAddDefinition(defId, defProxy.name);
    }
    var definitionMemberIds = model.Definitions.SelectMany(d => d.objects).ToHashSet(StringComparer.Ordinal);

    // Layer tier: flat CONTAINER("Layer") nodes, parentless (ENG-8965/ENG-9131).
    var layerContainers = new Dictionary<string, BundleContainer>(StringComparer.Ordinal);
    BundleContainer? Layer(string? levelKey)
    {
      if (levelKey == null)
      {
        return null;
      }
      if (!layerContainers.TryGetValue(levelKey, out BundleContainer? container))
      {
        container = bundle.GetOrAddContainer(levelKey, model.Levels[levelKey], null, "Layer");
        layerContainers[levelKey] = container;
      }
      return container;
    }

    // IN_MODEL federation: one CONTAINER("Model") per reference occurrence (dgnextract's per-
    // attachment containers). Active-model objects carry no model edge — single-model sends stay flat.
    var modelContainers = new Dictionary<string, BundleContainer>(StringComparer.Ordinal);
    foreach (var occurrence in model.Occurrences)
    {
      modelContainers[occurrence.Key] = bundle.GetOrAddContainer(
        $"model{occurrence.Key}",
        occurrence.Value,
        null,
        "Model"
      );
    }

    var materials = new Dictionary<string, BundleMaterial>(StringComparer.Ordinal);
    var results = model.Results.ToList();
    var resultIndexByAppId = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int i = 0; i < results.Count; i++)
    {
      resultIndexByAppId[results[i].SourceId] = i;
    }

    var memberGeometry = new Dictionary<string, List<ExtractedGeometry>>(StringComparer.Ordinal);
    var memberPlacements = new Dictionary<string, InstanceProxy>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      bool isMember = definitionMemberIds.Contains(co.ApplicationId);

      BundleObject obj = bundle.GetOrAddObject(co.ApplicationId);
      obj.SetProperties(co.Properties, name: co.Name, speckleType: SPECKLE_TYPE, sourceType: co.SourceType);
      if (Layer(co.LevelKey) is { } layer)
      {
        obj.Collection = layer;
      }
      if (co.OccurrenceTag.Length > 0 && modelContainers.TryGetValue(co.OccurrenceTag, out BundleContainer? mc))
      {
        obj.Model = mc;
      }

      string? dropReason = null;
      if (co.Proxy is InstanceProxy instanceProxy)
      {
        if (isMember)
        {
          memberPlacements[co.ApplicationId] = instanceProxy;
        }
        else
        {
          BundleDefinition def = definitions.TryGetValue(instanceProxy.definitionId, out BundleDefinition? d)
            ? d
            : bundle.GetOrAddDefinition(instanceProxy.definitionId, null);
          obj.Place(def, Flatten(instanceProxy.transform), instanceProxy.units, key: co.ApplicationId);
        }
      }
      else if (co.Geometry is { } extracted)
      {
        if (isMember)
        {
          memberGeometry[co.ApplicationId] = extracted;
        }
        else
        {
          dropReason = EmitGeometry(bundle, obj, extracted, materials);
        }
      }

      if (dropReason != null && resultIndexByAppId.TryGetValue(co.ApplicationId, out int ri))
      {
        results[ri] = new(Status.ERROR, co.ApplicationId, co.SourceType, null, new SpeckleException(dropReason));
      }
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    // Definition members: geometry rides DEFINES on the member's ordinal; nested placements
    // ride DEFINES_INSTANCE + PLACES (mirrors dgnextract's definition/member envelope).
    foreach (InstanceDefinitionProxy defProxy in model.Definitions)
    {
      BundleDefinition def = definitions[defProxy.applicationId!];
      foreach (string memberId in defProxy.objects)
      {
        if (memberPlacements.TryGetValue(memberId, out InstanceProxy? nestedProxy))
        {
          BundleObject member = bundle.GetOrAddObject(memberId);
          BundleDefinition nested = definitions.TryGetValue(nestedProxy.definitionId, out BundleDefinition? n)
            ? n
            : bundle.GetOrAddDefinition(nestedProxy.definitionId, null);
          def.AddMemberPlacement(member, nested, Flatten(nestedProxy.transform), nestedProxy.units);
        }
        else if (memberGeometry.TryGetValue(memberId, out List<ExtractedGeometry>? extracted))
        {
          BundleObject member = bundle.GetOrAddObject(memberId);
          int ord = def.NextMemberOrdinal();
          var encodable = new List<ExtractedGeometry>(extracted.Count);
          foreach (ExtractedGeometry g in extracted)
          {
            if (SgeoEncoder.TryGetPrimitiveType(g.Geometry, out _))
            {
              encodable.Add(g);
            }
            else
            {
              logger.LogWarning(
                "Skipped unsupported member display geometry {Type} on {AppId}",
                g.Geometry.speckle_type,
                memberId
              );
            }
          }
          IReadOnlyList<BundleGeometry> emitted = def.AddMember(member, encodable.Select(g => g.Geometry), ord);
          ApplyAppearance(bundle, emitted, encodable, materials);
        }
      }
    }

    return results;
  }

  /// <summary>Standalone object display geometry, with dgnextract's geometry-plane appearance:
  /// a real material XOR the symbology colour per fragment.</summary>
  private string? EmitGeometry(
    BundleBuilder bundle,
    BundleObject obj,
    List<ExtractedGeometry> extracted,
    Dictionary<string, BundleMaterial> materials
  )
  {
    string? lastSkip = null;
    int emittedCount = 0;
    var emitted = new List<BundleGeometry>(extracted.Count);
    var emittedSources = new List<ExtractedGeometry>(extracted.Count);
    foreach (ExtractedGeometry g in extracted)
    {
      try
      {
        BundleGeometry geometry = obj.AddGeometry(g.Geometry, g.Geometry.applicationId ?? $"{obj.ApplicationId}:g");
        emitted.Add(geometry);
        emittedSources.Add(g);
        emittedCount++;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        lastSkip = $"{g.Geometry.speckle_type}: {ex.Message}";
        logger.LogWarning(
          ex,
          "Skipped unsupported display geometry {Type} on {AppId}",
          g.Geometry.speckle_type,
          obj.ApplicationId
        );
      }
    }
    ApplyAppearance(bundle, emitted, emittedSources, materials);
    return extracted.Count > 0 && emittedCount == 0 ? lastSkip ?? "no display geometry could be encoded" : null;
  }

  private static void ApplyAppearance(
    BundleBuilder bundle,
    IReadOnlyList<BundleGeometry> emitted,
    IReadOnlyList<ExtractedGeometry> sources,
    Dictionary<string, BundleMaterial> materials
  )
  {
    int n = Math.Min(emitted.Count, sources.Count);
    for (int i = 0; i < n; i++)
    {
      ExtractedGeometry source = sources[i];
      if (source.Material is ResolvedMaterial material)
      {
        if (!materials.TryGetValue(material.Key, out BundleMaterial? bundleMaterial))
        {
          bundleMaterial = bundle.GetOrAddMaterial(material.Key, material.Name, material.Argb, material.Opacity, 0, 1);
          materials[material.Key] = bundleMaterial;
        }
        emitted[i].Material = bundleMaterial;
      }
      else if (source.ColorArgb is int argb)
      {
        emitted[i].Color = bundle.GetOrAddColor(argb);
      }
    }
  }

  private static double[] Flatten(Speckle.DoubleNumerics.Matrix4x4 m) =>
    [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];

  // ── pure-Speckle snapshot (phase 1 → phase 2) ────────────────────────────────────────────

  private sealed record CollectedObject(
    string ApplicationId,
    string Name,
    string SourceType,
    string? LevelKey,
    string OccurrenceTag,
    Dictionary<string, object?> Properties,
    InstanceProxy? Proxy,
    List<ExtractedGeometry>? Geometry
  );

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    IReadOnlyDictionary<string, string> Levels,
    IReadOnlyDictionary<string, string> Occurrences,
    IReadOnlyList<InstanceDefinitionProxy> Definitions,
    IReadOnlyList<SendConversionResult> Results
  );
}
