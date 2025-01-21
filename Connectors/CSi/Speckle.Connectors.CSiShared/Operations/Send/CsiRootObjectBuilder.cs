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
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettings;
  private readonly CsiSendCollectionManager _sendCollectionManager;
  private readonly MaterialUnpacker _materialUnpacker;
  private readonly ISectionUnpacker _sectionUnpacker;
  private readonly ISectionMaterialRelationshipManager _sectionMaterialRelationshipManager;
  private readonly IObjectSectionRelationshipManager _objectSectionRelationshipManager;
  private readonly List<Base> _convertedObjectsForProxies = []; // Not nice, but a way to store converted objects
  private readonly HashSet<string> _assignedFrameSectionIds = []; // Track which sections we NEED to create proxies for
  private readonly HashSet<string> _assignedShellSectionIds = []; // Track which sections we NEED to create proxies for
  private readonly HashSet<string> _assignedMaterialIds = []; // Track which materials we NEED to create proxies for
  private readonly ILogger<CsiRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ICsiApplicationService _csiApplicationService;

  public CsiRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<CsiConversionSettings> converterSettings,
    CsiSendCollectionManager sendCollectionManager,
    MaterialUnpacker materialUnpacker,
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
  ///
  /// _convertedObjectsForProxies notes:
  /// - SendConversionResult doesn't give us access to converted object
  /// - rootObjectCollection flattening seems a little unnecessary. We also don't need access to all converted objects
  /// - Only FRAME and SHELL have associated sections and thus need relations built
  /// - For this reason, these types are "true" and added to list
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

      // If object requires section relationship, collect both section and material names
      if (csiObject.RequiresSectionRelationship)
      {
        _convertedObjectsForProxies.Add(converted);
        AddMaterialAndSectionIdsToCache(converted);
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
    // TODO: Only unpack materials and sections which are assigned in the model
    try
    {
      using var activity = _activityFactory.Start("Process Proxies");

      var materials = _materialUnpacker.UnpackMaterials(rootObjectCollection, _assignedMaterialIds.ToArray());
      var sections = _sectionUnpacker.UnpackSections(
        rootObjectCollection,
        _assignedFrameSectionIds.ToArray(),
        _assignedShellSectionIds.ToArray()
      );

      _sectionMaterialRelationshipManager.EstablishRelationships(sections, materials);
      _objectSectionRelationshipManager.EstablishRelationships(_convertedObjectsForProxies, sections);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to process section and material proxies");
    }
  }

  /// <summary>
  /// Extracts section and material names from a converted object's assignments and adds them to their respective collections.
  /// This is only done for objects (FRAME and SHELL) where material - section - object relationships need to be est.
  /// Why do we need this? We only want to create proxies for assigned sections and materials.
  /// For this, we need to know what sections and materials have been assigned.
  /// </summary>
  /// <remarks>
  /// This method safely traverses the nested dictionary structure of the converted object to find:
  /// - sectionId under properties -> Assignments -> sectionId
  /// - materialId under properties -> Assignments -> materialId
  /// Both IDs are collected independently, as one can exist without the other.
  /// </remarks>
  private void AddMaterialAndSectionIdsToCache(Base converted)
  {
    // TODO: Improve. This is extremely brittle, but an appropriate workaround / interim solution!
    // Check if we can get the assignments dictionary
    if (
      converted["properties"] is not Dictionary<string, object?> { } properties
      || !properties.TryGetValue(ObjectPropertyCategory.ASSIGNMENTS, out var assignmentsObj)
      || assignmentsObj is not Dictionary<string, object?> { } assignments
    )
    {
      return;
    }

    // Get the object type
    var objectType = converted["type"]?.ToString();

    // Collect section IDs if they exist and are non-empty
    if (
      assignments.TryGetValue("sectionId", out var section)
      && section?.ToString() is { } sectionId
      && sectionId != "None"
    )
    {
      switch (objectType)
      {
        case var type when type == ModelObjectType.FRAME.ToString():
          _assignedFrameSectionIds.Add(sectionId);
          break;
        case var type when type == ModelObjectType.SHELL.ToString():
          _assignedShellSectionIds.Add(sectionId);
          break;
      }
    }

    // Collect material IDs if they exist and are non-empty
    if (
      assignments.TryGetValue("materialId", out var material)
      && material?.ToString() is { } materialId
      && materialId != "None"
    )
    {
      _assignedMaterialIds.Add(materialId);
    }
  }
}
