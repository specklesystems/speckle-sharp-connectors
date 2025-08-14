using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.Rhino.HostApp;
using Speckle.Connectors.Rhino.Mapper.Revit;

namespace Speckle.Connectors.Rhino.Bindings;

/// <summary>
/// Binding for managing Rhino object mappings to Revit categories.
/// </summary>
public class RhinoMapperBinding : IBinding
{
  private const string MAPPINGS_CHANGED_EVENT = "mappingsChanged";
  private const string LAYERS_CHANGED_EVENT = "layersChanged";
  private readonly DocumentModelStore _store;
  private readonly IAppIdleManager _idleManager;
  private readonly IBasicConnectorBinding _basicConnectorBinding;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly RevitMappingResolver _revitMappingResolver;
  private readonly RhinoLayerHelper _rhinoLayerHelper;
  private readonly RhinoObjectHelper _rhinoObjectHelper;
  public string Name => "revitMapperBinding";
  public IBrowserBridge Parent { get; }

  public RhinoMapperBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IBasicConnectorBinding basicConnectorBinding,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    RevitMappingResolver revitMappingResolver,
    RhinoLayerHelper rhinoLayerHelper,
    RhinoObjectHelper rhinoObjectHelper
  )
  {
    _store = store;
    _idleManager = idleManager;
    Parent = parent;
    _basicConnectorBinding = basicConnectorBinding;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _revitMappingResolver = revitMappingResolver;
    _rhinoLayerHelper = rhinoLayerHelper;
    _rhinoObjectHelper = rhinoObjectHelper;

    // Subscribe to Rhino events so we know about changes
    // Events fire on delete, undo delete and modify objects
    RhinoDoc.DeleteRhinoObject += OnObjectChanged;
    RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
    RhinoDoc.ModifyObjectAttributes += OnObjectAttributesChanged;

    // Subscribe to layer events so we know about layer changes
    RhinoDoc.LayerTableEvent += OnLayerTableEvent;

    // Subscribe to document changes to refresh mappings when switching documents
    _store.DocumentChanged += OnDocumentChanged;
  }

  #region UI Methods - General

  /// <summary>
  /// Gets list of available layers for the UI dropdown.
  /// </summary>
  public LayerOption[] GetAvailableLayers()
  {
    var doc = RhinoDoc.ActiveDoc;
    if (doc == null)
    {
      return Array.Empty<LayerOption>();
    }

    return doc
      .Layers.Where(layer => !layer.IsDeleted)
      .Select(layer => new LayerOption(layer.Id.ToString(), _rhinoLayerHelper.GetFullLayerPath(layer)))
      .OrderBy(layer => layer.Name)
      .ToArray();
  }

  /// <summary>
  /// Selects/highlights specific objects in Rhino.
  /// </summary>
  public async Task HighlightObjects(string[] objectIds) => await _basicConnectorBinding.HighlightObjects(objectIds);

  #endregion

  #region UI Methods - Object Mapping Methods

  /// <summary>
  /// Assigns selected objects to a specific Revit category.
  /// </summary>
  public void AssignObjectsToCategory(string[] objectIds, string categoryValue)
  {
    foreach (var objectIdString in objectIds)
    {
      // NOTE: should we be checking if key already exists?
      // For POC, straightforward set on object
      var rhinoObject = _rhinoObjectHelper.GetRhinoObject(objectIdString);
      var attrs = rhinoObject?.Attributes.Duplicate();
      attrs?.SetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY, categoryValue);
      RhinoDoc.ActiveDoc.Objects.ModifyAttributes(rhinoObject, attrs, true);
    }

    // Trigger single update after all changes
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Removes category assignments from specific objects.
  /// </summary>
  public void ClearObjectsCategoryAssignment(string[] objectIds)
  {
    foreach (var objectIdString in objectIds)
    {
      var rhinoObject = _rhinoObjectHelper.GetRhinoObject(objectIdString);
      var attrs = rhinoObject?.Attributes.Duplicate();
      attrs?.DeleteUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY);
      RhinoDoc.ActiveDoc.Objects.ModifyAttributes(rhinoObject, attrs, true);
    }

    // Trigger single update after all changes
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Removes all category assignments in the doc.
  /// </summary>
  public void ClearAllObjectsCategoryAssignments()
  {
    foreach (var rhinoObject in RhinoDoc.ActiveDoc.Objects)
    {
      if (!string.IsNullOrEmpty(rhinoObject.Attributes.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY)))
      {
        var attrs = rhinoObject.Attributes.Duplicate();
        attrs.DeleteUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY);
        RhinoDoc.ActiveDoc.Objects.ModifyAttributes(rhinoObject, attrs, true);
      }
    }

    // Trigger single update
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Gets all current mappings to show in the UI table.
  /// </summary>
  /// <returns></returns>
  public CategoryMapping[] GetCurrentObjectsMappings()
  {
    var mappedObjects = RhinoDoc
      .ActiveDoc.Objects.Where(obj =>
        !string.IsNullOrEmpty(obj.Attributes.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY))
      )
      .GroupBy(obj => obj.Attributes.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY))
      .Select(group => new CategoryMapping(
        group.Key,
        group.Key,
        group.Select(obj => obj.Id.ToString()).ToArray(),
        group.Count()
      ))
      .ToArray();

    return mappedObjects;
  }

  #endregion

  #region UI Methods - Layer Mapping Methods

  /// <summary>
  /// Assigns selected layers to a specific Revit category.
  /// </summary>
  public void AssignLayerToCategory(string[] layerIds, string categoryValue)
  {
    foreach (var layerId in layerIds)
    {
      var layer = _rhinoLayerHelper.GetLayer(layerId);
      layer?.SetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY, categoryValue);
    }

    // Trigger single update
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Removes category assignments from specific layer(s).
  /// </summary>
  public void ClearLayerCategoryAssignment(string[] layerIds)
  {
    foreach (var layerId in layerIds)
    {
      // NOTE: clear user string by setting to null. Layer has not DeleteUserString() method 🙄
      var layer = _rhinoLayerHelper.GetLayer(layerId);
      layer?.SetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY, null);
    }

    // Trigger single update
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Removes all layer category assignments in the doc.
  /// </summary>
  public void ClearAllLayerCategoryAssignments()
  {
    foreach (var layer in RhinoDoc.ActiveDoc.Layers)
    {
      if (!string.IsNullOrEmpty(layer.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY)))
      {
        // NOTE: clear user string by setting to null. Layer has not DeleteUserString() method 🙄
        layer.SetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY, null);
      }
    }

    // Trigger single update
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Gets all current layer mappings to show in the UI table.
  /// Layers with the same category mapping are grouped together.
  /// </summary>
  public LayerCategoryMapping[] GetCurrentLayerMappings()
  {
    var mappedLayers = RhinoDoc
      .ActiveDoc.Layers.Where(layer =>
        !string.IsNullOrEmpty(layer.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY))
      )
      .GroupBy(layer => layer.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY))
      .Select(group => new LayerCategoryMapping(
        group.Key,
        group.Key,
        group.Select(layer => layer.Id.ToString()).ToArray(),
        group.Select(layer => _rhinoLayerHelper.GetFullLayerPath(layer)).ToArray(),
        group.Count()
      ))
      .ToArray();

    return mappedLayers;
  }

  public string[] GetEffectiveObjectsForLayerMapping(string[] layerIds, string categoryValue) =>
    _revitMappingResolver.GetEffectiveObjectsForLayerMapping(layerIds, categoryValue);

  #endregion

  #region Event Handling

  /// <summary>
  /// Called when objects are deleted or undeleted in Rhino.
  /// </summary>
  private void OnObjectChanged(object? sender, RhinoObjectEventArgs e)
  {
    if (!_store.IsDocumentInit)
    {
      return;
    }

    var rhinoObject = e.TheObject;
    if (!string.IsNullOrEmpty(rhinoObject.Attributes.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY)))
    {
      _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
    }
  }

  /// <summary>
  /// Called when object attributes are modified in Rhino.
  /// </summary>
  /// <remarks>
  /// Includes detection for when objects move between layers with mappings.
  /// </remarks>
  private void OnObjectAttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs e)
  {
    if (!_store.IsDocumentInit)
    {
      return;
    }

    var rhinoObject = e.RhinoObject;

    // check if mapping user string changed (added, removed, or modified)
    var oldMapping = e.OldAttributes.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY);
    var newMapping = rhinoObject.Attributes.GetUserString(RevitMappingConstants.CATEGORY_USER_STRING_KEY);
    bool mappingChanged = !string.Equals(oldMapping, newMapping, StringComparison.Ordinal);

    // check if layer change affects mappings
    bool hasOldLayerMapping = _rhinoLayerHelper.HasLayerMapping(e.OldAttributes.LayerIndex);
    bool hasNewLayerMapping = _rhinoLayerHelper.HasLayerMapping(rhinoObject.Attributes.LayerIndex);

    // refresh if mapping changed OR layer change affects mapped layers
    if (mappingChanged || hasOldLayerMapping || hasNewLayerMapping)
    {
      _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
    }
  }

  /// <summary>
  /// Called when the document changes (e.g., switching to a different Rhino model).
  /// Refreshes the mappings table and defined layers to reflect the new document's state.
  /// </summary>
  private void OnDocumentChanged(object? sender, EventArgs e)
  {
    if (!_store.IsDocumentInit)
    {
      return;
    }

    // Refresh mappings for the new document
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);

    // Refresh layer list for the new document
    _idleManager.SubscribeToIdle(nameof(NotifyLayersChanged), NotifyLayersChanged);
  }

  /// <summary>
  /// Called when layer table events occur in Rhino.
  /// Refreshes layer list when layer structure changes.
  /// </summary>
  /// <remarks>
  /// Layer mapping changes are handled by OnObjectAttributesChanged
  /// </remarks>
  private void OnLayerTableEvent(object? sender, LayerTableEventArgs e) =>
    _topLevelExceptionHandler.CatchUnhandled(() =>
    {
      if (!_store.IsDocumentInit)
      {
        return;
      }

      // Refresh layer list for structural changes
      if (
        e.EventType == LayerTableEventType.Added
        || e.EventType == LayerTableEventType.Deleted
        || e.EventType == LayerTableEventType.Modified
      )
      {
        _idleManager.SubscribeToIdle(nameof(NotifyLayersChanged), NotifyLayersChanged);
      }
    });

  /// <summary>
  /// Sends updated mappings to the frontend.
  /// </summary>
  private void NotifyMappingsChanged()
  {
    var currentMappings = GetCurrentObjectsMappings();
    Parent.Send(MAPPINGS_CHANGED_EVENT, currentMappings);
  }

  /// <summary>
  /// Sends updated layer list to the frontend.
  /// </summary>
  private void NotifyLayersChanged()
  {
    var availableLayers = GetAvailableLayers();
    Parent.Send(LAYERS_CHANGED_EVENT, availableLayers);
  }

  #endregion
}
