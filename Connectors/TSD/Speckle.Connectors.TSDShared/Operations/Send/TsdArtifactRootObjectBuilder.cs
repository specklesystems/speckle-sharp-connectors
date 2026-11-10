using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Speckle.Common.StructuralExtrusion;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Converters.TSDShared;
using Speckle.Converters.TSDShared.Results;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using TSD.API.Remoting.Common;
using TSD.API.Remoting.Structure;
using Path = System.IO.Path;
using SpecContainer = Speckle.Bundle.Spec.Container;
using SpecLevel = Speckle.Bundle.Spec.Level;
using SpecStructuralResult = Speckle.Bundle.Spec.StructuralResult;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdArtifactRootObjectBuilder : IArtifactRootObjectBuilder<IEntity>
{
  private const string NODES_COLLECTION = "Nodes";
  private static readonly string[] s_noParts = Array.Empty<string>();
  private static readonly Dictionary<string, object?> s_emptyProps = new();

  private readonly ITSDApplicationService _applicationService;
  private readonly TsdEntitySnapshotBuilder _snapshotBuilder;
  private readonly TsdAnalysisResultsExtractor _analysisResultsExtractor;
  private readonly TsdConversionSettings _conversionSettings;
  private readonly ExtrusionFallbackTracker _extrusionFallbacks;
  private readonly IThreadContext _threadContext;
  private readonly IArtifactPipelineFactory _artifactPipelineFactory;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly ILogger<TsdArtifactRootObjectBuilder> _logger;

  public TsdArtifactRootObjectBuilder(
    ITSDApplicationService applicationService,
    TsdEntitySnapshotBuilder snapshotBuilder,
    TsdAnalysisResultsExtractor analysisResultsExtractor,
    TsdConversionSettings conversionSettings,
    ExtrusionFallbackTracker extrusionFallbacks,
    IThreadContext threadContext,
    IArtifactPipelineFactory artifactPipelineFactory,
    ISpeckleApplication speckleApplication,
    ILogger<TsdArtifactRootObjectBuilder> logger
  )
  {
    _applicationService = applicationService;
    _snapshotBuilder = snapshotBuilder;
    _analysisResultsExtractor = analysisResultsExtractor;
    _conversionSettings = conversionSettings;
    _extrusionFallbacks = extrusionFallbacks;
    _threadContext = threadContext;
    _artifactPipelineFactory = artifactPipelineFactory;
    _speckleApplication = speckleApplication;
    _logger = logger;
  }

  public async Task<ArtifactBuildResult> BuildAndUpload(
    IReadOnlyList<IEntity> objects,
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

    using var session = ArtefactSessionLog.Start("TSD", ArtefactDirection.Send, projectId, null, versionId, _logger);

    CollectedModel collected;
    using (session.Phase("Collect"))
    {
      collected = await CollectAsync(objects, session, onOperationProgressed, cancellationToken).ConfigureAwait(false);
    }

    return await _threadContext
      .RunOnWorkerAsync(async () =>
      {
        BundleResult built;
        using (session.Phase("Write"))
        {
          built = WriteBundle(collected, session, versionId, outputDir, onOperationProgressed, cancellationToken);
        }

        using var pipeline = _artifactPipelineFactory.CreateInstance(
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
      })
      .ConfigureAwait(false);
  }

  private async Task<CollectedModel> CollectAsync(
    IReadOnlyList<IEntity> entities,
    ArtefactSessionLog session,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var modelUnits = await _snapshotBuilder.GetUnitsAsync().ConfigureAwait(false);
    var slabsByIndex = await _snapshotBuilder.GetSlabsAsync(entities).ConfigureAwait(false);

    var collected = new List<CollectedObject>(entities.Count);
    var results = new List<SendConversionResult>(entities.Count);
    var propertyTrees = new List<Dictionary<string, object?>>(entities.Count);

    int count = 0;
    foreach (var entity in entities)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();

      var (result, converted) = await _snapshotBuilder
        .ConvertEntityAsync(entity, slabsByIndex, modelUnits.LengthUnit, modelUnits.SpeckleUnits)
        .ConfigureAwait(false);
      results.Add(result);
      if (converted is not null)
      {
        collected.Add(new CollectedObject(converted.applicationId ?? entity.Id.ToString(), converted.type, converted));
        propertyTrees.Add(converted.properties);
        session.RecordObject(result.SourceId, result.SourceType, Status.SUCCESS, null, sw.ElapsedMilliseconds);
      }
      else
      {
        session.RecordObject(
          result.SourceId,
          result.SourceType,
          Status.ERROR,
          result.Error?.Message,
          sw.ElapsedMilliseconds
        );
      }

      onOperationProgressed.Report(new("Converting", (double)++count / entities.Count));
    }

    if (results.Count > 0 && results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    if (_extrusionFallbacks.Total > 0)
    {
      _logger.LogWarning(
        "Volumetric geometry kept the wireframe display value for {FallbackCount} element(s): {@FallbackCounts}",
        _extrusionFallbacks.Total,
        _extrusionFallbacks.Counts
      );
      session.SetStat("extrusionFallbacks", _extrusionFallbacks.Total);
    }

    using (session.Phase("Slabs"))
    {
      collected = GroupSlabItems(collected, slabsByIndex, results, propertyTrees, session, modelUnits.SpeckleUnits);
    }

    await _snapshotBuilder.ApplyUnitsAsync(propertyTrees, modelUnits.Units).ConfigureAwait(false);

    NodeRelations relations;
    using (session.Phase("Nodes"))
    {
      relations = await CollectNodesAsync(collected, results, modelUnits, session, cancellationToken)
        .ConfigureAwait(false);
    }

    List<StructuralResultRow> resultRows;
    using (session.Phase("Analysis results"))
    {
      resultRows = await ExtractResultRowsAsync(modelUnits, session, cancellationToken).ConfigureAwait(false);
    }

    return new CollectedModel(modelUnits.SpeckleUnits, collected, relations, resultRows, results);
  }

  // A slab item hangs off its slab as SUBELEMENT, ord = its place in the slab's own item list; the slab is a
  // geometry-less parent carrying the slab-level data. An item whose slab did not come back stays top level
  // (ENG-11157).
  private List<CollectedObject> GroupSlabItems(
    List<CollectedObject> collected,
    IReadOnlyDictionary<int, ISlab> slabsByIndex,
    List<SendConversionResult> results,
    List<Dictionary<string, object?>> propertyTrees,
    ArtefactSessionLog session,
    string speckleUnits
  )
  {
    var placements = _snapshotBuilder.SlabItemPlacementByAppId;
    var output = new List<CollectedObject>(collected.Count);
    var parents = new Dictionary<int, (TsdObject Slab, List<(int Ord, TsdObject Item)> Items)>();
    foreach (CollectedObject co in collected)
    {
      if (
        !placements.TryGetValue(co.ApplicationId, out var placement)
        || !slabsByIndex.TryGetValue(placement.SlabIndex, out var slab)
      )
      {
        output.Add(co);
        continue;
      }

      if (!parents.TryGetValue(placement.SlabIndex, out var parent))
      {
        parent = (_snapshotBuilder.ConvertSlabParent(slab, speckleUnits), new List<(int, TsdObject)>());
        parents[placement.SlabIndex] = parent;
      }

      int ord = OrdOf(slab.SlabItemIndices, placement.ItemIndex);
      parent.Items.Add((ord < 0 ? int.MaxValue : ord, co.Object));
    }

    foreach (var (slab, items) in parents.Values)
    {
      slab.elements.AddRange(items.OrderBy(item => item.Ord).Select(item => item.Item));
      string appId = slab.applicationId ?? slab.name ?? slab.type;
      output.Add(new CollectedObject(appId, slab.type, slab));
      results.Add(new SendConversionResult(Status.SUCCESS, appId, slab.type, slab));
      propertyTrees.Add(slab.properties);
      session.RecordObject(appId, slab.type, Status.SUCCESS, null, 0);
    }

    session.SetStat("slabs", parents.Count);
    return output;
  }

  private static int OrdOf(IReadOnlyList<int> order, int index)
  {
    for (int i = 0; i < order.Count; i++)
    {
      if (order[i] == index)
      {
        return i;
      }
    }

    return -1;
  }

  /// <summary>Every collected object and its descendants: slab items and wall panels sit below their parent.</summary>
  private static IEnumerable<string> AllAppIds(IEnumerable<CollectedObject> collected) =>
    collected.SelectMany(co => Descendants(co.Object)).Select(obj => obj.applicationId).OfType<string>();

  private static IEnumerable<TsdObject> Descendants(TsdObject obj)
  {
    yield return obj;
    foreach (TsdObject child in obj.elements)
    {
      foreach (TsdObject descendant in Descendants(child))
      {
        yield return descendant;
      }
    }
  }

  // Only construction points a sent entity sits on become node objects (the ETABS auto-joint filter, ENG-10192), so
  // every node is the target of at least one CONNECTS_TO and no edge dangles (ENG-11155).
  private async Task<NodeRelations> CollectNodesAsync(
    List<CollectedObject> collected,
    List<SendConversionResult> results,
    TsdModelUnits modelUnits,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var edges = new List<NodeEdge>();
    var indicesByAppId = _snapshotBuilder.NodeIndicesByAppId;
    var referenced = new HashSet<int>();
    foreach (string appId in AllAppIds(collected))
    {
      if (indicesByAppId.TryGetValue(appId, out var indices))
      {
        referenced.UnionWith(indices);
      }
    }

    IReadOnlyList<(int Index, TsdObject Node)> nodes;
    try
    {
      nodes = await _snapshotBuilder
        .ConvertConstructionPointsAsync(referenced, modelUnits.LengthUnit, modelUnits.SpeckleUnits, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Skipped TSD construction points");
      session.RecordObject("construction-points", NODES_COLLECTION, Status.WARNING, ex.Message, 0);
      return new NodeRelations(
        edges,
        Array.Empty<TsdLevel>(),
        Array.Empty<LevelAssignment>(),
        CollectGroups(collected)
      );
    }

    var nodeAppIdByIndex = new Dictionary<int, string>(nodes.Count);
    foreach (var (index, node) in nodes)
    {
      string appId = node.applicationId ?? index.ToString(CultureInfo.InvariantCulture);
      nodeAppIdByIndex[index] = appId;
      collected.Add(new CollectedObject(appId, NODES_COLLECTION, node));
      results.Add(new SendConversionResult(Status.SUCCESS, appId, node.type, node));
      session.RecordObject(appId, node.type, Status.SUCCESS, null, 0);
    }

    foreach (string appId in AllAppIds(collected))
    {
      if (!indicesByAppId.TryGetValue(appId, out var indices))
      {
        continue;
      }
      foreach (int index in indices)
      {
        if (nodeAppIdByIndex.TryGetValue(index, out var nodeAppId))
        {
          edges.Add(new NodeEdge(appId, nodeAppId));
        }
      }
    }

    session.SetStat("nodes", nodes.Count);
    session.SetStat("connectsTo", edges.Count);
    if (referenced.Count != nodes.Count)
    {
      _logger.LogWarning(
        "TSD returned {Returned} of {Referenced} referenced construction points",
        nodes.Count,
        referenced.Count
      );
    }

    var (levels, onLevel) = await CollectLevelsAsync(collected, modelUnits, session, cancellationToken)
      .ConfigureAwait(false);
    return new NodeRelations(edges, levels, onLevel, CollectGroups(collected));
  }

  // ON_LEVEL only when every construction point of the element sits on one horizontal construction plane: beams,
  // slab items and single-storey walls do, columns and braces span planes and get no invented level (ENG-11156).
  private async Task<(IReadOnlyList<TsdLevel> Levels, List<LevelAssignment> OnLevel)> CollectLevelsAsync(
    IReadOnlyList<CollectedObject> collected,
    TsdModelUnits modelUnits,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var indicesByAppId = _snapshotBuilder.NodeIndicesByAppId;
    var planeByNodeIndex = _snapshotBuilder.HorizontalPlaneByNodeIndex;
    var onLevel = new List<LevelAssignment>();
    foreach (string appId in AllAppIds(collected))
    {
      if (indicesByAppId.TryGetValue(appId, out var indices) && CommonPlane(indices, planeByNodeIndex) is int plane)
      {
        onLevel.Add(new LevelAssignment(appId, plane));
      }
    }

    IReadOnlyList<TsdLevel> levels = Array.Empty<TsdLevel>();
    if (onLevel.Count > 0)
    {
      try
      {
        levels = await _snapshotBuilder
          .ConvertLevelsAsync(
            onLevel.Select(a => a.PlaneIndex).Distinct().ToList(),
            modelUnits.LengthUnit,
            cancellationToken
          )
          .ConfigureAwait(false);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Skipped TSD levels");
        session.RecordObject("levels", "Level", Status.WARNING, ex.Message, 0);
        onLevel.Clear();
      }
    }

    session.SetStat("levels", levels.Count);
    session.SetStat("onLevel", onLevel.Count);
    return (levels, onLevel);
  }

  private static int? CommonPlane(IReadOnlyList<int> indices, IReadOnlyDictionary<int, int> planeByNodeIndex)
  {
    int? common = null;
    foreach (int index in indices)
    {
      if (!planeByNodeIndex.TryGetValue(index, out int plane) || (common is int known && known != plane))
      {
        return null;
      }
      common = plane;
    }
    return common;
  }

  private IReadOnlyDictionary<string, string> CollectGroups(IReadOnlyList<CollectedObject> collected)
  {
    var groupByAppId = _snapshotBuilder.GroupByAppId;
    var groups = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (string appId in AllAppIds(collected))
    {
      if (groupByAppId.TryGetValue(appId, out var group))
      {
        groups[appId] = group;
      }
    }
    return groups;
  }

  private async Task<List<StructuralResultRow>> ExtractResultRowsAsync(
    TsdModelUnits modelUnits,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var loadings = _conversionSettings.SelectedLoadings;
    var resultTypes = _conversionSettings.SelectedResultTypes;

    TsdResultsPayload? payload;
    try
    {
      payload = await _analysisResultsExtractor
        .ExtractAsync(loadings, resultTypes, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "TSD analysis result extraction skipped");
      session.RecordObject("analysis-results", "AnalysisResults", Status.WARNING, ex.Message, 0);
      return new List<StructuralResultRow>();
    }

    if (payload is null)
    {
      _logger.LogWarning(
        "TSD structural results NOT extracted: SelectedLoadings={LoadingCount}, SelectedResultTypes={ResultTypeCount} — both must be non-empty. Check the model card's Loadings & Result Types settings.",
        loadings.Count,
        resultTypes.Count
      );
      session.RecordObject(
        "analysis-results",
        "AnalysisResults",
        Status.WARNING,
        $"results not extracted — selected loadings: {loadings.Count}, selected result types: {resultTypes.Count} (both must be > 0)",
        0
      );
      return new List<StructuralResultRow>();
    }

    var pending = new List<PendingResultRow>();
    foreach (var (resultType, loadingsObj) in payload.Tree)
    {
      if (loadingsObj is not IDictionary<string, object?> loadingBranch)
      {
        continue;
      }
      // Only element-keyed result types carry solver element indices. Nodal results are keyed by construction point
      // index — a different space that overlaps numerically — so resolving those here would link the wrong member.
      bool elementKeyed = Array.IndexOf(TsdElementIndexMap.ElementKeyedResultTypes, resultType) >= 0;
      foreach (var (loadCase, entitiesObj) in loadingBranch)
      {
        if (entitiesObj is not IDictionary<string, object?> entityBranch)
        {
          continue;
        }
        foreach (var (key, sub) in entityBranch)
        {
          // Resolved → object-level row (object_index set, location null). Unresolved → left model-level under its
          // raw key rather than guessing at an object.
          string? objectAppId =
            elementKeyed && int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementIndex)
              ? payload.Elements.GetEntityId(elementIndex)
              : null;
          WalkResult(
            resultType,
            loadCase,
            objectAppId is null ? key : null,
            objectAppId,
            sub,
            s_noParts,
            null,
            pending
          );
        }
      }
    }

    var rows = new List<StructuralResultRow>(pending.Count);
    foreach (var group in pending.GroupBy(p => p.Value.Quantity))
    {
      var items = group.ToList();
      modelUnits.Units.TryGetValue(group.Key, out var unit);

      IReadOnlyList<double> values = unit is null
        ? items.Select(i => i.Value.BaseValue).ToList()
        : await _applicationService
          .ConvertFromBaseAsync(items.Select(i => i.Value.BaseValue).ToList(), unit)
          .ConfigureAwait(false);

      for (int i = 0; i < items.Count; i++)
      {
        var p = items[i];
        double value = i < values.Count ? values[i] : p.Value.BaseValue;
        rows.Add(
          new StructuralResultRow(
            p.ObjectAppId,
            p.Location,
            p.ResultType,
            p.LoadCase,
            p.Component,
            p.Station,
            null,
            value
          )
        );
      }
    }

    session.SetStat("resultRows", rows.Count);
    if (rows.Count == 0)
    {
      _logger.LogWarning(
        "TSD structural results extraction ran for {Loadings} loading(s) x {Types} result type(s) but produced 0 rows — is the analysis run and do the selected loadings have results?",
        loadings.Count,
        resultTypes.Count
      );
    }
    return rows;
  }

  private static void WalkResult(
    string resultType,
    string loadCase,
    string? location,
    string? objectAppId,
    object? node,
    IReadOnlyList<string> pathParts,
    double? station,
    List<PendingResultRow> sink
  )
  {
    if (node is not IDictionary<string, object?> dict)
    {
      return;
    }

    if (dict.Values.Any(v => v is TsdQuantityValue))
    {
      foreach (var (componentKey, value) in dict)
      {
        if (value is TsdQuantityValue quantityValue)
        {
          string component = pathParts.Count == 0 ? componentKey : string.Join(".", pathParts) + "." + componentKey;
          sink.Add(
            new PendingResultRow(resultType, loadCase, location, objectAppId, component, station, quantityValue)
          );
        }
      }
      return;
    }

    foreach (var (key, child) in dict)
    {
      if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var stationValue))
      {
        WalkResult(resultType, loadCase, location, objectAppId, child, pathParts, stationValue, sink);
      }
      else
      {
        var next = new List<string>(pathParts) { key };
        WalkResult(resultType, loadCase, location, objectAppId, child, next, station, sink);
      }
    }
  }

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
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId, producer: _speckleApplication);
    var collectionKByName = new Dictionary<string, int>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddCollection(pipeline, co.Type, collectionKByName);
      EmitObject(pipeline, co.Object, collK, model.Units);
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

    // Unscoped (ord 0): a member or area element sits on its nodes, there is no second edge set to tell apart (ADR-0002).
    foreach (NodeEdge edge in model.Relations.Edges)
    {
      pipeline.ConnectsTo(pipeline.InternObject(edge.SourceAppId), pipeline.InternObject(edge.NodeAppId));
    }

    // Levels and groups are separate axes from the by-type collections, which stay the default view (ENG-11156).
    var levelKByIndex = new Dictionary<int, int>();
    foreach (TsdLevel level in model.Relations.Levels)
    {
      levelKByIndex[level.Index] = pipeline.AddLevel(
        $"level:{level.Index}",
        new SpecLevel(level.Name, level.Elevation)
      );
    }
    foreach (LevelAssignment assignment in model.Relations.OnLevel)
    {
      if (levelKByIndex.TryGetValue(assignment.PlaneIndex, out int levelK))
      {
        pipeline.OnLevel(pipeline.InternObject(assignment.AppId), levelK);
      }
    }

    var groupKByName = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var (appId, group) in model.Relations.Groups)
    {
      if (!groupKByName.TryGetValue(group, out int groupK))
      {
        groupK = pipeline.AddContainer($"group:{group}", new SpecContainer(group, null, "Group", null));
        groupKByName[group] = groupK;
      }
      pipeline.InGroup(pipeline.InternObject(appId), groupK, 0);
    }
    session.SetStat("groups", groupKByName.Count);

    foreach (var r in model.ResultRows)
    {
      pipeline.AddStructuralResult(
        r.ObjectAppId,
        new SpecStructuralResult(
          ObjectIndex: null,
          ElementName: null,
          Location: r.Location,
          ResultType: r.ResultType,
          LoadCase: r.LoadCase,
          Component: r.Component,
          Station: r.Station,
          Step: r.Step,
          Value: r.Value
        )
      );
    }

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
    session.SetStat("collections", collectionKByName.Count);
    return new BundleResult(bundle, rootId, objectCount, model.Results);
  }

  private void EmitObject(
    ObjectsArtifactPipeline pipeline,
    TsdObject obj,
    int collK,
    string units,
    int? parentObjK = null,
    int subelementOrd = 0
  )
  {
    string appId = obj.applicationId ?? Guid.NewGuid().ToString();
    int objK = pipeline.InternObject(appId);
    pipeline.InCollection(objK, collK, 0);
    if (parentObjK is int pK)
    {
      pipeline.Subelement(pK, objK, subelementOrd);
    }

    pipeline.AddProperties(
      appId,
      obj.properties ?? s_emptyProps,
      RootScalars(obj.speckle_type, obj.name, units, obj.type)
    );

    int ord = 0;
    foreach (Base fragment in obj.displayValue)
    {
      try
      {
        string gAppId = fragment.applicationId ?? $"{appId}:g{ord}";
        int gK = pipeline.AddGeometry(gAppId, fragment);
        pipeline.Display(objK, gK, ord++);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Skipped unsupported display geometry {Type} on {AppId}", fragment.speckle_type, appId);
      }
    }

    if (_snapshotBuilder.CenterlinesByAppId.TryGetValue(appId, out var centerlines))
    {
      for (int span = 0; span < centerlines.Count; span++)
      {
        try
        {
          // Own key per span: sharing one with a display fragment would collapse DISPLAY and CENTERLINE onto one blob.
          pipeline.Centerline(objK, pipeline.AddGeometry($"{appId}:cl{span}", centerlines[span]), span);
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogWarning(ex, "Skipped centerline geometry on {AppId}", appId);
        }
      }
    }

    int childOrd = 0;
    foreach (TsdObject child in obj.elements)
    {
      EmitObject(pipeline, child, collK, units, objK, childOrd++);
    }
  }

  private static int GetOrAddCollection(ObjectsArtifactPipeline pipeline, string name, Dictionary<string, int> cache)
  {
    var key = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
    if (cache.TryGetValue(key, out var existing))
    {
      return existing;
    }
    int collK = pipeline.AddCollection(key, new SpecContainer(key, null, "Collection", null));
    cache[key] = collK;
    return collK;
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

  private sealed record PendingResultRow(
    string ResultType,
    string LoadCase,
    string? Location,
    string? ObjectAppId,
    string Component,
    double? Station,
    TsdQuantityValue Value
  );

  private sealed record StructuralResultRow(
    string? ObjectAppId,
    string? Location,
    string ResultType,
    string LoadCase,
    string Component,
    double? Station,
    int? Step,
    double? Value
  );

  private sealed record CollectedObject(string ApplicationId, string Type, TsdObject Object);

  private sealed record NodeEdge(string SourceAppId, string NodeAppId);

  private sealed record LevelAssignment(string AppId, int PlaneIndex);

  private sealed record NodeRelations(
    IReadOnlyList<NodeEdge> Edges,
    IReadOnlyList<TsdLevel> Levels,
    IReadOnlyList<LevelAssignment> OnLevel,
    IReadOnlyDictionary<string, string> Groups
  );

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
    NodeRelations Relations,
    IReadOnlyList<StructuralResultRow> ResultRows,
    IReadOnlyList<SendConversionResult> Results
  );

  private sealed record BundleResult(
    IReadOnlyDictionary<string, string> Bundle,
    string RootId,
    int ObjectCount,
    IReadOnlyList<SendConversionResult> Results
  );
}
