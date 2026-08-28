using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.TSDShared.HostApp;
using Speckle.Converters.TSDShared;
using Speckle.Converters.TSDShared.Results;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;
using TSD.API.Remoting.Common;

namespace Speckle.Connectors.TSDShared.Operations.Send;

internal sealed class TsdRootObjectBuilder : IRootObjectBuilder<IEntity>
{
  private readonly ITSDApplicationService _applicationService;
  private readonly TsdEntitySnapshotBuilder _snapshotBuilder;
  private readonly TsdAnalysisResultsExtractor _analysisResultsExtractor;
  private readonly TsdSendCollectionManager _sendCollectionManager;
  private readonly TsdConversionSettings _conversionSettings;

  public TsdRootObjectBuilder(
    ITSDApplicationService applicationService,
    TsdEntitySnapshotBuilder snapshotBuilder,
    TsdAnalysisResultsExtractor analysisResultsExtractor,
    TsdConversionSettings conversionSettings
    TsdSendCollectionManager sendCollectionManager,
    TsdConversionSettings conversionSettings,
    ILogger<TsdRootObjectBuilder> logger
  )
  {
    _applicationService = applicationService;
    _snapshotBuilder = snapshotBuilder;
    _analysisResultsExtractor = analysisResultsExtractor;
    _sendCollectionManager = sendCollectionManager;
    _conversionSettings = conversionSettings;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<IEntity> entities,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    var modelUnits = await _snapshotBuilder.GetUnitsAsync().ConfigureAwait(false);

    Collection rootObjectCollection = new()
    {
      name = _applicationService.ApplicationTitle ?? "Tekla Structural Designer",
    };
    rootObjectCollection["units"] = modelUnits.SpeckleUnits;

    var slabDataByIndex = await _snapshotBuilder.GetSlabDataAsync(entities).ConfigureAwait(false);

    List<SendConversionResult> results = new(entities.Count);
    List<Dictionary<string, object?>> propertyTrees = new(entities.Count);
    int count = 0;

    foreach (var entity in entities)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var (result, converted) = await _snapshotBuilder
        .ConvertEntityAsync(entity, slabDataByIndex, modelUnits.LengthUnit, modelUnits.SpeckleUnits)
        .ConfigureAwait(false);
      results.Add(result);
      if (converted is not null)
      {
        var collection = _sendCollectionManager.AddObjectCollectionToRoot(converted, rootObjectCollection);
        collection.elements.Add(converted);
        if (converted is TsdObject tsdObject)
        {
          propertyTrees.Add(tsdObject.properties);
        }
      }

      count++;
      onOperationProgressed.Report(new CardProgress("Converting", (double)count / entities.Count));
    }

    Dictionary<string, object?>? analysisResultsTree;
    try
    {
      // v3 path publishes the tree as-is; the payload's element map is only meaningful to the artefact path
      analysisResultsTree = (
        await _analysisResultsExtractor
          .ExtractAsync(
            _conversionSettings.SelectedLoadings,
            _conversionSettings.SelectedResultTypes,
            cancellationToken
          )
          .ConfigureAwait(false)
      )?.Tree;
    }
    catch (SpeckleException)
    {
      throw;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      throw new SpeckleException("Analysis result extraction failed", ex);
    }

    if (analysisResultsTree is not null)
    {
      foreach (var resultBranch in analysisResultsTree.Values)
      {
        if (resultBranch is Dictionary<string, object?> branch)
        {
          propertyTrees.Add(branch);
        }
      }
    }

    await _snapshotBuilder.ApplyUnitsAsync(propertyTrees, modelUnits.Units).ConfigureAwait(false);

    if (analysisResultsTree is not null)
    {
      var analysisResults = new Base();
      foreach (var (key, value) in analysisResultsTree)
      {
        analysisResults[key] = value;
      }

      rootObjectCollection[RootKeys.ANALYSIS_RESULTS] = analysisResults;
    }

    if (results.Count > 0 && results.TrueForAll(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }
}
