using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Logging;
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
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using TSD.API.Remoting.Common;
using Path = System.IO.Path;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdArtifactRootObjectBuilder : IArtifactRootObjectBuilder<IEntity>
{
  private static readonly string[] s_noParts = Array.Empty<string>();
  private static readonly Dictionary<string, object?> s_emptyProps = new();

  private readonly ITSDApplicationService _applicationService;
  private readonly TsdEntitySnapshotBuilder _snapshotBuilder;
  private readonly TsdAnalysisResultsExtractor _analysisResultsExtractor;
  private readonly TsdConversionSettings _conversionSettings;
  private readonly IThreadContext _threadContext;
  private readonly IArtifactPipelineFactory _artifactPipelineFactory;
  private readonly ILogger<TsdArtifactRootObjectBuilder> _logger;

  public TsdArtifactRootObjectBuilder(
    ITSDApplicationService applicationService,
    TsdEntitySnapshotBuilder snapshotBuilder,
    TsdAnalysisResultsExtractor analysisResultsExtractor,
    TsdConversionSettings conversionSettings,
    IThreadContext threadContext,
    IArtifactPipelineFactory artifactPipelineFactory,
    ILogger<TsdArtifactRootObjectBuilder> logger
  )
  {
    _applicationService = applicationService;
    _snapshotBuilder = snapshotBuilder;
    _analysisResultsExtractor = analysisResultsExtractor;
    _conversionSettings = conversionSettings;
    _threadContext = threadContext;
    _artifactPipelineFactory = artifactPipelineFactory;
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
    var slabDataByIndex = await _snapshotBuilder.GetSlabDataAsync(entities).ConfigureAwait(false);

    var collected = new List<CollectedObject>(entities.Count);
    var results = new List<SendConversionResult>(entities.Count);
    var propertyTrees = new List<Dictionary<string, object?>>(entities.Count);

    int count = 0;
    foreach (var entity in entities)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();

      var (result, converted) = await _snapshotBuilder
        .ConvertEntityAsync(entity, slabDataByIndex, modelUnits.LengthUnit, modelUnits.SpeckleUnits)
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

    await _snapshotBuilder.ApplyUnitsAsync(propertyTrees, modelUnits.Units).ConfigureAwait(false);

    List<StructuralResultRow> resultRows;
    using (session.Phase("Analysis results"))
    {
      resultRows = await ExtractResultRowsAsync(modelUnits, session, cancellationToken).ConfigureAwait(false);
    }

    return new CollectedModel(modelUnits.SpeckleUnits, collected, resultRows, results);
  }

  private async Task<List<StructuralResultRow>> ExtractResultRowsAsync(
    TsdModelUnits modelUnits,
    ArtefactSessionLog session,
    CancellationToken cancellationToken
  )
  {
    var loadings = _conversionSettings.SelectedLoadings;
    var resultTypes = _conversionSettings.SelectedResultTypes;

    Dictionary<string, object?>? tree;
    try
    {
      tree = await _analysisResultsExtractor
        .ExtractAsync(loadings, resultTypes, cancellationToken)
        .ConfigureAwait(false);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "TSD analysis result extraction skipped");
      session.RecordObject("analysis-results", "AnalysisResults", Status.WARNING, ex.Message, 0);
      return new List<StructuralResultRow>();
    }

    if (tree is null)
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
    foreach (var (resultType, loadingsObj) in tree)
    {
      if (loadingsObj is not IDictionary<string, object?> loadingBranch)
      {
        continue;
      }
      foreach (var (loadCase, entitiesObj) in loadingBranch)
      {
        if (entitiesObj is not IDictionary<string, object?> entityBranch)
        {
          continue;
        }
        foreach (var (location, sub) in entityBranch)
        {
          WalkResult(resultType, loadCase, location, sub, s_noParts, null, pending);
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
          new StructuralResultRow(null, p.Location, p.ResultType, p.LoadCase, p.Component, p.Station, null, value)
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
    string location,
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
          sink.Add(new PendingResultRow(resultType, loadCase, location, component, station, quantityValue));
        }
      }
      return;
    }

    foreach (var (key, child) in dict)
    {
      if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out var stationValue))
      {
        WalkResult(resultType, loadCase, location, child, pathParts, stationValue, sink);
      }
      else
      {
        var next = new List<string>(pathParts) { key };
        WalkResult(resultType, loadCase, location, child, next, station, sink);
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
    using var pipeline = new ObjectsArtifactPipeline(outputDir, versionId);
    var collectionKByName = new Dictionary<string, int>(StringComparer.Ordinal);

    int count = 0;
    foreach (CollectedObject co in model.Objects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      int collK = GetOrAddCollection(pipeline, co.Type, collectionKByName);
      EmitObject(pipeline, co.Object, collK, model.Units);
      onOperationProgressed.Report(new("Building", (double)++count / model.Objects.Count));
    }

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
        r.Value
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
    int collK = pipeline.AddCollection(key, key, null, "Collection");
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
    string Location,
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

  private sealed record CollectedModel(
    string Units,
    IReadOnlyList<CollectedObject> Objects,
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
