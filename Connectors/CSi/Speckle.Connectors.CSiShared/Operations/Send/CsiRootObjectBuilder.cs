using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Extensions;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
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
  private readonly MaterialUnpacker _materialUnpacker;
  private readonly ISectionUnpacker _sectionUnpacker;
  private readonly ILogger<CsiRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly DatabaseTableExtractor _databaseTableExtractor;

  public CsiRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    IConverterSettingsStore<CsiConversionSettings> converterSettings,
    CsiSendCollectionManager sendCollectionManager,
    MaterialUnpacker materialUnpacker,
    ISectionUnpacker sectionUnpacker,
    ILogger<CsiRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ICsiApplicationService csiApplicationService,
    DatabaseTableExtractor databaseTableExtractor
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
    _databaseTableExtractor = databaseTableExtractor;
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
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");

    string modelFileName = _csiApplicationService.SapModel.GetModelFilename(false) ?? "Unnamed model";
    Collection rootObjectCollection =
      new() { name = modelFileName, ["units"] = _converterSettings.Current.SpeckleUnits };

    List<SendConversionResult> results = new(csiObjects.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (ICsiWrapper csiObject in csiObjects)
      {
        cancellationToken.ThrowIfCancellationRequested();
        using var _2 = _activityFactory.Start("Convert");

        var result = ConvertCsiObject(csiObject, rootObjectCollection);
        results.Add(result);

        count++;
        onOperationProgressed.Report(new("Converting", (double)count / csiObjects.Count));
        await Task.Yield();
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    using (var _ = _activityFactory.Start("Process Proxies"))
    {
      // Create and add material proxies
      rootObjectCollection[ProxyKeys.MATERIAL] = _materialUnpacker.UnpackMaterials().ToList();

      // Create and all section proxies (frame and shell)
      rootObjectCollection[ProxyKeys.SECTION] = _sectionUnpacker.UnpackSections().ToList();
    }

    // sending all analysis results for now
    // hackady-hack-hack-hack
    Base analysisPlaceholder = new();
    var analysisResults = _databaseTableExtractor.GetTableData("Element Forces - Columns").Rows;
    analysisPlaceholder["analysisResults"] = analysisResults;
    rootObjectCollection["analysisPlaceholder"] = analysisPlaceholder;

    // refactored into a class [BS - today]
    // reduce data noise - limit to only send axial forces for now? [BS - today]
    // need to handle the numerous stations and load combinations associated with a single column. we have the issue that since keys are unique, we only get one entry for a column. nested nested nested dict ? or concatenate the key? [DK / BS]
    // ui settings - multi-select on load cases / combinations to send [DK - tomorrow]
    // ui setting to send results (just limit to column forces for now) [DK - tomorrow]
    // controls - is analysis run? are results available? send only column results for the columns that are selected [BS - today]
    // sample poc automate function [DK & BS]

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
      _ => throw new ArgumentException($"Unsupported wrapper type: {csiObject.GetType()}", nameof(csiObject))
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
      _logger.LogError(ex, sourceType);
      return new(Status.WARNING, applicationId, sourceType, null, ex);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
