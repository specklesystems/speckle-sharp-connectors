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

namespace Speckle.Connectors.CSiShared.Builders;

/// <summary>
/// Manages the conversion of CSi model objects and establishes proxy-based relationships.
/// </summary>
/// <remarks>
/// Core responsibilities:
/// - Converts ICsiWrappers to Speckle objects through caching-aware conversion
/// - Creates proxy objects for materials and sections from model data
/// - Establishes relationships between objects and their dependencies
///
/// The builder follows a two-phase process:
/// 1. Conversion Phase: ICsiWrappers → Speckle objects with cached results handling
/// 2. Relationship Phase: Material/section proxy creation and relationship mapping
/// </remarks>
public class CsiRootObjectBuilder : IRootObjectBuilder<ICsiWrapper>
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

  public CsiRootObjectBuilder(
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

  /// <summary>
  /// Converts Csi objects into a Speckle-compatible object hierarchy with established relationships.
  /// </summary>
  /// <remarks>
  /// Operation sequence:
  /// 1. Creates root collection with model metadata
  /// 2. Converts each object with caching and progress tracking
  /// 3. Creates proxies for materials and sections
  /// </remarks>
  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ICsiWrapper> csiObjects,
    string projectId,
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
        ["temperatureUnits"] = tempUnit,
      };

    List<SendConversionResult> results = new(csiObjects.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (ICsiWrapper csiObject in csiObjects)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ConvertCsiObject(csiObject, rootObjectCollection);
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
      // Create and add material proxies
      rootObjectCollection[ProxyKeys.MATERIAL] = _materialUnpacker.UnpackMaterials().ToList();

      // Create and all section proxies (frame and shell)
      rootObjectCollection[ProxyKeys.SECTION] = _sectionUnpacker.UnpackSections().ToList();
    }

    // Extract analysis results (if applicable)
    // NOTE: objectSelectionSummary used to extract results for objects being published ONLY
    // NOTE: etabs is complicated and we can't get specifics from original selection
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

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  /// <summary>
  /// Converts a single Csi wrapper "object" to a data object with appropriate collection management.
  /// </summary>
  private SendConversionResult ConvertCsiObject(ICsiWrapper csiObject, Collection typeCollection)
  {
    string sourceType = csiObject.ObjectName;
    string applicationId = csiObject switch
    {
      CsiJointWrapper jointWrapper => jointWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiFrameWrapper frameWrapper => frameWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiCableWrapper cableWrapper => cableWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiTendonWrapper tendonWrapper => tendonWrapper.ObjectName, // No GetGUID method in the Csi API available
      CsiShellWrapper shellWrapper => shellWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiSolidWrapper solidWrapper => solidWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      CsiLinkWrapper linkWrapper => linkWrapper.GetSpeckleApplicationId(_csiApplicationService.SapModel),
      _ => throw new ArgumentException($"Unsupported wrapper type: {csiObject.GetType()}", nameof(csiObject)),
    };

    try
    {
      Base converted = _rootToSpeckleConverter.Convert(csiObject);

      var collection = _sendCollectionManager.AddObjectCollectionToRoot(converted, typeCollection);
      collection.elements.Add(converted);

      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    // Expected not implemented:
    // TODO: SAP 2000: CsiCableWrapper, CsiSolidWrapper
    // TODO: ETABS: CsiLinkWrapper, CsiTendonWrapper
    // NOTE: CsiLinkWrapper - not important to data extraction workflow
    // NOTE: CsiTendonWrapper - not typically modelled in ETABS, rather SAFE
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

  /// <summary>
  /// Generates a summary of object types and their associated names from the collection of CSI wrappers.
  /// </summary>
  /// <remarks>
  /// A summary of object names for each object type is needed for getting analysis results of the selected objects only.
  /// During object conversion, however, we lose the selection (like a clear selection)(presumably because of other api calls).
  /// This has to be recreated since GetSelection() return type is bound by the interface.
  /// The LINQ-based implementation is computationally inexpensive as it operates on an already-loaded collection without additional API calls.
  /// Also, we don't want to rely on user selection remaining active, what if someone re-publishes using model card cache?
  /// </remarks>
  private Dictionary<ModelObjectType, List<string>> GetObjectSummary(IReadOnlyList<ICsiWrapper> csiObjects) =>
    csiObjects
      .GroupBy(csiObject => csiObject.ObjectType)
      .ToDictionary(
        group => group.Key, // ModelObjectType (FRAME, JOINT, etc.)
        group => group.Select(obj => obj.Name).ToList() // Extract Name from each ICsiWrapper and convert to List<string>
      );

  /// <summary>
  /// Instantiates a Base object and pre-populates it with the models defined force units.
  /// </summary>
  /// <returns></returns>
  /// <exception cref="SpeckleException"></exception>
  private (string, string) GetForceAndTemperatureUnits()
  {
    var forceUnit = eForce.NotApplicable;
    var lengthUnit = eLength.NotApplicable;
    var temperatureUnit = eTemperature.NotApplicable;

    _converterSettings.Current.SapModel.GetDatabaseUnits_2(ref forceUnit, ref lengthUnit, ref temperatureUnit);

    return (forceUnit.ToString(), temperatureUnit.ToString());
  }
}
