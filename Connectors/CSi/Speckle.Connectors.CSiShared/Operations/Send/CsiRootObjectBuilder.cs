using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.CSiShared.HostApp.Relationships;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
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
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettings;
  private readonly CsiSendCollectionManager _sendCollectionManager;
  private readonly IMaterialUnpacker _materialUnpacker;
  private readonly ISectionUnpacker _sectionUnpacker;
  private readonly ISectionMaterialRelationshipManager _sectionMaterialRelationshipManager;
  private readonly IObjectSectionRelationshipManager _objectSectionRelationshipManager;
  private readonly List<Base> _convertedObjectsForProxies = [];
  private readonly ILogger<CsiRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ICsiApplicationService _csiApplicationService;

  public CsiRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<CsiConversionSettings> converterSettings,
    CsiSendCollectionManager sendCollectionManager,
    IMaterialUnpacker materialUnpacker,
    ISectionUnpacker sectionUnpacker,
    ISectionMaterialRelationshipManager sectionMaterialRelationshipManager,
    IObjectSectionRelationshipManager objectSectionRelationshipManager,
    ILogger<CsiRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ICsiApplicationService csiApplicationService
  )
  {
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
    _materialUnpacker = materialUnpacker;
    _sectionUnpacker = sectionUnpacker;
    _sectionMaterialRelationshipManager = sectionMaterialRelationshipManager;
    _objectSectionRelationshipManager = objectSectionRelationshipManager;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _csiApplicationService = csiApplicationService;
  }

  /// <summary>
  /// Converts CSi objects into a Speckle-compatible object hierarchy with established relationships.
  /// </summary>
  /// <remarks>
  /// Operation sequence:
  /// 1. Creates root collection with model metadata
  /// 2. Converts each object with caching and progress tracking
  /// 3. Processes material/section relationships if conversion successful
  /// </remarks>
  public async Task<RootObjectBuilderResult> BuildAsync(
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

        var result = ConvertCsiObject(csiObject, rootObjectCollection, sendInfo.ProjectId);
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
      ProcessProxies(rootObjectCollection);
    }

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  /// <summary>
  /// Converts a single CSi object with caching and collection management.
  /// </summary>
  /// <remarks>
  /// Conversion process:
  /// 1. Checks conversion cache for existing result
  /// 2. Performs conversion if not cached
  /// 3. Adds to type-specific collection
  /// 4. Tracks objects needing section relationships
  /// </remarks>
  private SendConversionResult ConvertCsiObject(ICsiWrapper csiObject, Collection typeCollection, string projectId)
  {
    string applicationId = $"{csiObject.ObjectType}{csiObject.Name}"; // TODO: NO! Use GUID
    string sourceType = csiObject.ObjectName;

    try
    {
      Base converted;
      if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
      {
        converted = value;
      }
      else
      {
        converted = _rootToSpeckleConverter.Convert(csiObject);
      }

      var collection = _sendCollectionManager.AddObjectCollectionToRoot(converted, typeCollection);
      collection.elements.Add(converted);

      if (csiObject.RequiresSectionRelationship)
      {
        _convertedObjectsForProxies.Add(converted);
      }

      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }

  /// <summary>
  /// Creates proxy objects and establishes object relationships.
  /// </summary>
  /// <remarks>
  /// Processing sequence:
  /// 1. Creates material proxies (independent objects)
  /// 2. Creates section proxies (may reference materials)
  /// 3. Establishes section-material relationships
  /// 4. Maps converted objects to their sections
  /// Relationships are managed through specialized managers for clear responsibility separation.
  /// </remarks>
  private void ProcessProxies(Collection rootObjectCollection)
  {
    try
    {
      using var activity = _activityFactory.Start("Process Proxies");

      var materialProxies = _materialUnpacker.UnpackMaterials(rootObjectCollection);
      var sectionProxies = _sectionUnpacker.UnpackSections(rootObjectCollection);

      _sectionMaterialRelationshipManager.EstablishRelationships(sectionProxies, materialProxies);
      _objectSectionRelationshipManager.EstablishRelationships(_convertedObjectsForProxies, sectionProxies);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to process section and material proxies");
    }
  }
}
