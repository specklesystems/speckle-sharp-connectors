using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.CSiShared.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Send;

namespace Speckle.Connectors.CSiShared.Builders;

/// <summary>
/// Continuous traversal builder for CSi that streams objects through a <see cref="SendPipeline"/>
/// for packfile-based uploads. Same conversion logic as <see cref="CsiRootObjectBuilder"/>.
/// </summary>
public class CsiContinuousTraversalBuilder : IRootContinuousTraversalBuilder<ICsiWrapper>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettings;
  private readonly CsiSendCollectionManager _sendCollectionManager;
  private readonly IMaterialUnpacker _materialUnpacker;
  private readonly ISectionUnpacker _sectionUnpacker;
  private readonly ILogger<CsiRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly AnalysisResultsExtractor _analysisResultsExtractor;

  public CsiContinuousTraversalBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    IConverterSettingsStore<CsiConversionSettings> converterSettings,
    CsiSendCollectionManager sendCollectionManager,
    IMaterialUnpacker materialUnpacker,
    ISectionUnpacker sectionUnpacker,
    ILogger<CsiRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ICsiApplicationService csiApplicationService,
    AnalysisResultsExtractor analysisResultsExtractor
  )
  {
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
    _materialUnpacker = materialUnpacker;
    _sectionUnpacker = sectionUnpacker;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _csiApplicationService = csiApplicationService;
    _analysisResultsExtractor = analysisResultsExtractor;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ICsiWrapper> csiObjects,
    string projectId,
    SendPipeline sendPipeline,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");

    string modelFileName = _csiApplicationService.SapModel.GetModelFilename(false) ?? "Unnamed model";
    (string forceUnit, string tempUnit) = GetForceAndTemperatureUnits();

    Collection rootObjectCollection =
      new()
      {
        name = modelFileName,
        ["units"] = _converterSettings.Current.SpeckleUnits,
        ["forceUnits"] = forceUnit,
        ["temperatureUnits"] = tempUnit
      };

    List<SendConversionResult> results = new(csiObjects.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (ICsiWrapper csiObject in csiObjects)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await ConvertCsiObject(csiObject, rootObjectCollection, sendPipeline);
        results.Add(result);

        count++;
        onOperationProgressed.Report(new("Converting", (double)count / csiObjects.Count));
        await Task.Yield();
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects");
    }

    using (var _ = _activityFactory.Start("Process Proxies"))
    {
      rootObjectCollection[ProxyKeys.MATERIAL] = _materialUnpacker.UnpackMaterials().ToList();
      rootObjectCollection[ProxyKeys.SECTION] = _sectionUnpacker.UnpackSections().ToList();
    }

    // Extract analysis results (if applicable)
    var objectSelectionSummary = GetObjectSummary(csiObjects);
    var selectedCasesAndCombinations = _converterSettings.Current.SelectedLoadCasesAndCombinations;
    var requestedResultTypes = _converterSettings.Current.SelectedResultTypes;

    if (selectedCasesAndCombinations?.Count > 0)
    {
      if (requestedResultTypes == null || requestedResultTypes.Count == 0)
      {
        throw new SpeckleException(
          "Adjust publish settings - no result type input for the requested load cases and combinations"
        );
      }

      if (!_csiApplicationService.SapModel.GetModelIsLocked())
      {
        throw new SpeckleException("Model unlocked, no access to analysis results");
      }

      try
      {
        var analysisResults = _analysisResultsExtractor.ExtractAnalysisResults(
          selectedCasesAndCombinations,
          requestedResultTypes,
          objectSelectionSummary
        );
        rootObjectCollection[RootKeys.ANALYSIS_RESULTS] = analysisResults;
      }
      catch (Exception ex)
      {
        throw new SpeckleException("Analysis result extraction failed", ex);
      }
    }

    // Process root collection and wait for all uploads
    await sendPipeline.Process(rootObjectCollection);
    await sendPipeline.WaitForUpload();

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private async Task<SendConversionResult> ConvertCsiObject(
    ICsiWrapper csiObject,
    Collection typeCollection,
    SendPipeline sendPipeline
  )
  {
    string sourceType = csiObject.ObjectName;
    string applicationId = csiObject switch
    {
      CsiJointWrapper jointWrapper => jointWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiFrameWrapper frameWrapper => frameWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiCableWrapper cableWrapper => cableWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiTendonWrapper tendonWrapper => tendonWrapper.ObjectName,
      CsiShellWrapper shellWrapper => shellWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiSolidWrapper solidWrapper => solidWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiLinkWrapper linkWrapper => linkWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      _ => throw new ArgumentException($"Unsupported wrapper type: {csiObject.GetType()}", nameof(csiObject))
    };

    try
    {
      Base converted = _rootToSpeckleConverter.Convert(csiObject);

      var collection = _sendCollectionManager.AddObjectCollectionToRoot(converted, typeCollection);

      // NOTE: this is the main part that differentiate from the main root object builder
      var reference = await sendPipeline.Process(converted).ConfigureAwait(false);
      collection.elements.Add(reference);

      return new(Status.SUCCESS, applicationId, sourceType, reference);
    }
    catch (NotImplementedException ex)
    {
      _logger.LogError(ex, "Failed to convert object {sourceType}", sourceType);
      return new(Status.WARNING, applicationId, sourceType, null, ex);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert object {sourceType}", sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }

  private Dictionary<ModelObjectType, List<string>> GetObjectSummary(IReadOnlyList<ICsiWrapper> csiObjects) =>
    csiObjects
      .GroupBy(csiObject => csiObject.ObjectType)
      .ToDictionary(group => group.Key, group => group.Select(obj => obj.Name).ToList());

  private (string, string) GetForceAndTemperatureUnits()
  {
    var forceUnit = eForce.NotApplicable;
    var lengthUnit = eLength.NotApplicable;
    var temperatureUnit = eTemperature.NotApplicable;

    _converterSettings.Current.SapModel.GetDatabaseUnits_2(ref forceUnit, ref lengthUnit, ref temperatureUnit);

    return (forceUnit.ToString(), temperatureUnit.ToString());
  }
}
