using Rhino;
using Rhino.DocObjects;
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
/// Binding for managing Rhino object mappings to Revit categories.
/// </summary>
public class RhinoMapperBinding : IBinding
{
  private readonly DocumentModelStore _store;
  private readonly IAppIdleManager _idleManager;
  private readonly IBasicConnectorBinding _basicConnectorBinding;
  private const string CATEGORY_USER_STRING_KEY = "builtInCategory";
  private const string MAPPINGS_CHANGED_EVENT = "mappingsChanged";
  public string Name => "revitMapperBinding";
  public IBrowserBridge Parent { get; }

  public RhinoMapperBinding(
    DocumentModelStore store,
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IBasicConnectorBinding basicConnectorBinding
  )
  {
    _store = store;
    _idleManager = idleManager;
    Parent = parent;
    _basicConnectorBinding = basicConnectorBinding;

    // Subscribe to Rhino events so we know about changes
    // Events fire on delete, undo delete and modify objects
    RhinoDoc.DeleteRhinoObject += OnObjectChanged;
    RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
    RhinoDoc.ModifyObjectAttributes += OnObjectAttributesChanged;

    // Subscribe to document changes to refresh mappings when switching documents
    _store.DocumentChanged += OnDocumentChanged;
  }

  #region UI Methods

  /// <summary>
  /// Gets list of available Revit categories for the UI dropdown.
  /// </summary>
  public CategoryOption[] GetAvailableCategories() =>
    RevitBuiltInCategoryStore.Categories.OrderBy(category => category.Label).ToArray();

  /// <summary>
  /// Assigns selected objects to a specific Revit category.
  /// </summary>
  public void AssignToCategory(string[] objectIds, string categoryValue)
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
  public void ClearCategoryAssignment(string[] objectIds)
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
  public void ClearAllCategoryAssignments()
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
  public CategoryMapping[] GetCurrentMappings()
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

  /// <summary>
  /// Selects/highlights specific objects in Rhino.
  /// </summary>
  public async Task HighlightObjects(string[] objectIds) => await _basicConnectorBinding.HighlightObjects(objectIds);

  /// <summary>
  /// Converts a string object ID to a RhinoObject.
  /// </summary>
  /// <returns>RhinoObject if found and valid, null otherwise</returns>
  /// <remarks>Reducing repetitive code.</remarks>
  private static RhinoObject? GetRhinoObject(string objectIdString) =>
    Guid.TryParse(objectIdString, out var objectId) ? RhinoDoc.ActiveDoc.Objects.FindId(objectId) : null;

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
  private void OnObjectAttributesChanged(object? sender, RhinoModifyObjectAttributesEventArgs e) // Fixed: Correct event signature
  {
    var rhinoObject = e.RhinoObject;
    if (!string.IsNullOrEmpty(rhinoObject.Attributes.GetUserString(CATEGORY_USER_STRING_KEY)))
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
  /// Sends updated mappings to the frontend.
  /// </summary>
  private void NotifyMappingsChanged()
  {
    var currentMappings = GetCurrentMappings();
    Parent.Send(MAPPINGS_CHANGED_EVENT, currentMappings);
  }

  #endregion
}
