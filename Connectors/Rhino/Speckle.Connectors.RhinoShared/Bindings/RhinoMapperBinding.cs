using Rhino;
using Rhino.DocObjects;
using Rhino.DocObjects.Tables;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.Rhino.Mapper.Revit;

namespace Speckle.Connectors.Rhino.Bindings;

/// <summary>
/// Represents a group of objects that are all assigned to the same category.
/// </summary>
public record CategoryMapping(
  string CategoryValue,
  string CategoryLabel,
  IReadOnlyList<string> ObjectIds,
  int ObjectCount
);

/// <summary>
/// Represents layers that are all assigned to the same category.
/// </summary>
public record LayerCategoryMapping(
  string CategoryValue,
  string CategoryLabel,
  IReadOnlyList<string> LayerIds,
  IReadOnlyList<string> LayerNames,
  int LayerCount
);

/// <summary>
/// Binding for managing Rhino object mappings to Revit categories.
/// </summary>
public class RhinoMapperBinding : IBinding
{
  private readonly DocumentModelStore _store;
  private readonly IAppIdleManager _idleManager;
  private readonly IBasicConnectorBinding _basicConnectorBinding;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private const string CATEGORY_USER_STRING_KEY = "builtInCategory";
  private const string MAPPINGS_CHANGED_EVENT = "mappingsChanged";
  public string Name => "revitMapperBinding";
  public IBrowserBridge Parent { get; }

  public RhinoMapperBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IBasicConnectorBinding basicConnectorBinding,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    _store = store;
    _idleManager = idleManager;
    Parent = parent;
    _basicConnectorBinding = basicConnectorBinding;
    _topLevelExceptionHandler = topLevelExceptionHandler;

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
  /// Gets list of available Revit categories for the UI dropdown.
  /// </summary>
  public CategoryOption[] GetAvailableCategories() =>
    RevitBuiltInCategoryStore.Categories.OrderBy(category => category.Label).ToArray();

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
      var rhinoObject = GetRhinoObject(objectIdString);
      rhinoObject?.Attributes.SetUserString(CATEGORY_USER_STRING_KEY, categoryValue);
      rhinoObject?.CommitChanges();
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
      // NOTE: should we be checking if key already exists?
      // For POC, straightforward delete on object
      var rhinoObject = GetRhinoObject(objectIdString);
      rhinoObject?.Attributes.DeleteUserString(CATEGORY_USER_STRING_KEY);
      rhinoObject?.CommitChanges();
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
      if (!string.IsNullOrEmpty(rhinoObject.Attributes.GetUserString(CATEGORY_USER_STRING_KEY)))
      {
        rhinoObject.Attributes.DeleteUserString(CATEGORY_USER_STRING_KEY);
        rhinoObject.CommitChanges();
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
      .ActiveDoc.Objects.Where(obj => !string.IsNullOrEmpty(obj.Attributes.GetUserString(CATEGORY_USER_STRING_KEY)))
      .GroupBy(obj => obj.Attributes.GetUserString(CATEGORY_USER_STRING_KEY))
      .Select(group => new CategoryMapping(
        group.Key,
        RevitBuiltInCategoryStore.GetLabel(group.Key),
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
      var layer = GetLayer(layerId);
      if (layer != null)
      {
        layer.SetUserString(CATEGORY_USER_STRING_KEY, categoryValue);
      }
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
      var layer = GetLayer(layerId);
      if (layer != null)
      {
        // NOTE: clear user string by setting to null. Layer has not DeleteUserString() method 🙄
        layer.SetUserString(CATEGORY_USER_STRING_KEY, null);
      }
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
      if (!string.IsNullOrEmpty(layer.GetUserString(CATEGORY_USER_STRING_KEY)))
      {
        // NOTE: clear user string by setting to null. Layer has not DeleteUserString() method 🙄
        layer.SetUserString(CATEGORY_USER_STRING_KEY, null);
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
      .ActiveDoc.Layers.Where(layer => !string.IsNullOrEmpty(layer.GetUserString(CATEGORY_USER_STRING_KEY)))
      .GroupBy(layer => layer.GetUserString(CATEGORY_USER_STRING_KEY))
      .Select(group => new LayerCategoryMapping(
        group.Key,
        RevitBuiltInCategoryStore.GetLabel(group.Key),
        group.Select(layer => layer.Id.ToString()).ToArray(),
        group.Select(layer => GetFullLayerPath(layer)).ToArray(),
        group.Count()
      ))
      .ToArray();

    return mappedLayers;
  }

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
    if (!string.IsNullOrEmpty(rhinoObject.Attributes.GetUserString(CATEGORY_USER_STRING_KEY)))
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

    // Check if object has direct mapping or if old/new layers have mappings
    bool hasObjectMapping = !string.IsNullOrEmpty(rhinoObject.Attributes.GetUserString(CATEGORY_USER_STRING_KEY));
    bool hasOldLayerMapping = HasLayerMapping(e.OldAttributes.LayerIndex);
    bool hasNewLayerMapping = HasLayerMapping(rhinoObject.Attributes.LayerIndex);

    // Refresh if object has mapping or if layer change affects mapped layers
    if (hasObjectMapping || hasOldLayerMapping || hasNewLayerMapping)
    {
      _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
    }
  }

  /// <summary>
  /// Called when the document changes (e.g., switching to a different Rhino model).
  /// Refreshes the mappings table to reflect the new document's state.
  /// </summary>
  private void OnDocumentChanged(object? sender, EventArgs e)
  {
    if (!_store.IsDocumentInit)
    {
      return;
    }

    // Refresh mappings for the new document
    _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
  }

  /// <summary>
  /// Called when layer table events occur in Rhino.
  /// Follows same pattern as RhinoSendBinding for layer events.
  /// </summary>
  private void OnLayerTableEvent(object? sender, LayerTableEventArgs e) =>
    _topLevelExceptionHandler.CatchUnhandled(() =>
    {
      if (!_store.IsDocumentInit)
      {
        return;
      }

      // Skip certain event types (same as RhinoSendBinding)
      if (
        e.EventType == LayerTableEventType.Deleted
        || e.EventType == LayerTableEventType.Current
        || e.EventType == LayerTableEventType.Added
      )
      {
        return;
      }

      var layer = RhinoDoc.ActiveDoc.Layers[e.LayerIndex];
      // Only refresh if this layer has a mapping or if we're modifying layer properties
      if (!string.IsNullOrEmpty(layer.GetUserString(CATEGORY_USER_STRING_KEY)))
      {
        _idleManager.SubscribeToIdle(nameof(NotifyMappingsChanged), NotifyMappingsChanged);
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

  #endregion

  #region Helper Methods

  /// <summary>
  /// Converts a string object ID to a RhinoObject.
  /// </summary>
  /// <returns>RhinoObject if found and valid, null otherwise</returns>
  /// <remarks>Reducing repetitive code.</remarks>
  private static RhinoObject? GetRhinoObject(string objectIdString) =>
    Guid.TryParse(objectIdString, out var objectId) ? RhinoDoc.ActiveDoc.Objects.FindId(objectId) : null;

  /// <summary>
  /// Converts a string layer ID to a Layer.
  /// </summary>
  /// <returns>Layer if found and valid, null otherwise</returns>
  private static Layer? GetLayer(string layerIdString) =>
    Guid.TryParse(layerIdString, out var layerId) ? RhinoDoc.ActiveDoc.Layers.FindId(layerId) : null;

  /// <summary>
  /// Gets the full layer path with / delimiter (matching send workflow).
  /// Reuses the exact same logic as RhinoLayersFilter.GetFullLayerPath().
  /// </summary>
  private static string GetFullLayerPath(Layer layer)
  {
    string fullPath = layer.Name;
    Guid parentIndex = layer.ParentLayerId;
    while (parentIndex != Guid.Empty)
    {
      Layer? parentLayer = RhinoDoc.ActiveDoc.Layers.FindId(parentIndex);
      if (parentLayer == null)
      {
        break;
      }

      fullPath = parentLayer.Name + "/" + fullPath; // use "/" delimiter like send workflow
      parentIndex = parentLayer.ParentLayerId;
    }
    return fullPath;
  }

  /// <summary>
  /// Helper to check if a layer (by index) has a category mapping.
  /// </summary>
  private static bool HasLayerMapping(int layerIndex)
  {
    if (layerIndex < 0 || layerIndex >= RhinoDoc.ActiveDoc.Layers.Count)
    {
      return false;
    }

    var layer = RhinoDoc.ActiveDoc.Layers[layerIndex];
    return !string.IsNullOrEmpty(layer.GetUserString(CATEGORY_USER_STRING_KEY));
  }

  #endregion
}
