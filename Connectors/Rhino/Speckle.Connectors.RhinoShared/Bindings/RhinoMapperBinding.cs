using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Rhino.HostApp;

namespace Speckle.Connectors.Rhino.Bindings;

/// <summary>
/// Represents a group of objects that are all assigned to the same category.
/// </summary>
/// <param name="CategoryValue">The Revit category enum name</param>
/// <param name="CategoryLabel">Human-readable category name</param>
/// <param name="ObjectIds">Array of Rhino object IDs assigned to this category</param>
/// <param name="ObjectCount">Number of objects (for convenience)</param>
public record CategoryMapping(string CategoryValue, string CategoryLabel, string[] ObjectIds, int ObjectCount);

/// <summary>
/// Binding for managing Rhino object mappings to Revit categories.
/// </summary>
public class RhinoMapperBinding : IBinding
{
  private readonly IAppIdleManager _idleManager; // updates happen safely without crashing rhino
  private readonly IBasicConnectorBinding _basicConnectorBinding; // object highlight functionality
  private const string CATEGORY_USER_STRING_KEY = "revitMapping"; // appear as key on rhino objects with mapped category
  private const string MAPPINGS_CHANGED_EVENT = "mappingsChanged"; // event name sent on change
  public string Name => "revitMapperBinding"; // unique id that ui calls
  public IBrowserBridge Parent { get; } // communication channel

  public RhinoMapperBinding(
    IAppIdleManager idleManager,
    IBrowserBridge parent,
    IBasicConnectorBinding basicConnectorBinding
  )
  {
    _idleManager = idleManager;
    Parent = parent;
    _basicConnectorBinding = basicConnectorBinding;

    // Subscribe to Rhino events so we know about changes
    // Events fire on delete, undo delete and modify objects
    RhinoDoc.DeleteRhinoObject += OnObjectChanged;
    RhinoDoc.UndeleteRhinoObject += OnObjectChanged;
    RhinoDoc.ModifyObjectAttributes += OnObjectChanged;
  }

  #region UI-Callable Methods

  /// <summary>
  /// Gets list of available Revit categories for the UI dropdown
  /// </summary>
  public async Task<CategoryOption[]> GetAvailableCategories() => RevitBuiltInCategoryStore.Categories;

  /// <summary>
  /// Assigns selected objects to a specific Revit category
  /// </summary>
  public async Task AssignToCategory(string[] objectIds, string categoryValue)
  {
    var doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return; // or throw here?
    }

    // Is this really the best way?
    foreach (var objectIdString in objectIds)
    {
      var rhinoObject = GetRhinoObject(doc, objectIdString);
      if (rhinoObject is not null)
      {
        // NOTE: should we be checking if key already exists?
        // For POC, straightforward set on object
        rhinoObject.Attributes.SetUserString(CATEGORY_USER_STRING_KEY, categoryValue);
        rhinoObject.CommitChanges();
      }
    }
  }

  /// <summary>
  /// Removes category assignments from specific objects.
  /// </summary>
  public async Task ClearCategoryAssignment(string[] objectIds)
  {
    var doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return; // or throw here?
    }

    // Is this really the best way?
    foreach (var objectIdString in objectIds)
    {
      var rhinoObject = GetRhinoObject(doc, objectIdString);
      if (rhinoObject is not null)
      {
        // NOTE: should we be checking if key already exists?
        // For POC, straightforward delete on object
        rhinoObject.Attributes.DeleteUserString(CATEGORY_USER_STRING_KEY);
        rhinoObject.CommitChanges();
      }
    }
  }

  /// <summary>
  /// Removes all category assignments in the doc.
  /// </summary>
  public async Task ClearAllCategoryAssignments()
  {
    var doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return;
    }

    // Or should we rather be getting a flattened list of objectIds from mappings table?
    foreach (var rhinoObject in doc.Objects)
    {
      var categoryValue = rhinoObject.Attributes.GetUserString(CATEGORY_USER_STRING_KEY);
      if (!string.IsNullOrEmpty(categoryValue))
      {
        rhinoObject.Attributes.DeleteUserString(CATEGORY_USER_STRING_KEY);
        rhinoObject.CommitChanges();
      }
    }
  }

  /// <summary>
  /// Gets all current mappings to show in the UI table.
  /// </summary>
  /// <returns></returns>
  public async Task<CategoryMapping[]> GetCurrentMappings()
  {
    var doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return [];
    }

    // Step 1: Find objects with mappings
    var mappedObjects = doc
      .Objects.Where(obj => !string.IsNullOrEmpty(obj.Attributes.GetUserString(CATEGORY_USER_STRING_KEY)))
      .ToList();

    // Step 2: Group by category value and create CategoryMappings
    var categoryMappings = mappedObjects
      .GroupBy(obj => obj.Attributes.GetUserString(CATEGORY_USER_STRING_KEY))
      .Select(group => new CategoryMapping(
        group.Key, // categoryValue
        RevitBuiltInCategoryStore.GetLabel(group.Key), // categoryLabel
        group.Select(obj => obj.Id.ToString()).ToArray(), // objectIds
        group.Count() // objectCount
      ))
      .ToArray();

    return categoryMappings;
  }

  /// <summary>
  /// Get all objects assigned to a specific category
  /// </summary>
  public async Task<string[]> GetObjectsByCategory(string categoryValue)
  {
    var doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return [];
    }

    var objectIds = doc
      .Objects.Where(obj => obj.Attributes.GetUserString(CATEGORY_USER_STRING_KEY) == categoryValue)
      .Select(obj => obj.Id.ToString())
      .ToArray();

    return objectIds;
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
  private static RhinoObject? GetRhinoObject(RhinoDoc doc, string objectIdString) =>
    !Guid.TryParse(objectIdString, out Guid objectId) ? null : doc.Objects.FindId(objectId);

  #endregion

  #region Event Handling

  /// <summary>
  /// Called when objects are changed in Rhino.
  /// </summary>
  private void OnObjectChanged(object? sender, RhinoObjectEventArgs e)
  {
    // TODO
  }

  /// <summary>
  /// Sends updated mappings to the frontend.
  /// </summary>
  private void NotifyMappingsChanged()
  {
    // TODO
  }

  #endregion
}
