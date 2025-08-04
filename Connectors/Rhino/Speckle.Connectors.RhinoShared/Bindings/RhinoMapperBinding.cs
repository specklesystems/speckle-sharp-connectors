using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Rhino.Bindings;

/// <summary>
/// Represents a category option for the dropdown in the UI.
/// </summary>
/// <param name="Value">The Revit category enum name (e.g., "OST_Walls")</param>
/// <param name="Label">Human-readable name for the UI (e.g., "Walls")</param>
public record CategoryOption(string Value, string Label);

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
  public async Task<CategoryOption[]> GetAvailableCategories()
  {
    // TODO
    return Array.Empty<CategoryOption>();
  }

  /// <summary>
  /// Assigns selected objects to a specific Revit category
  /// </summary>
  public async Task AssignToCategory(string[] objectIds, string categoryValue)
  {
    // TODO
  }

  /// <summary>
  /// Removes category assignments from specific objects.
  /// </summary>
  public async Task ClearCategoryAssignment(string[] objectIds)
  {
    // TODO
  }

  /// <summary>
  /// Removes all category assignments in the doc.
  /// </summary>
  public async Task ClearAllCategoryAssignments()
  {
    // TODO
  }

  /// <summary>
  /// Gets all current mappings to show in the UI table.
  /// </summary>
  /// <returns></returns>
  public async Task<CategoryMapping[]> GetCurrentMappings()
  {
    // TODO
    return Array.Empty<CategoryMapping>();
  }

  /// <summary>
  /// Get all objects assigned to a specific category
  /// </summary>
  public async Task<string[]> GetObjectsByCategory(string categoryValue)
  {
    // TODO
    return Array.Empty<string>();
  }

  /// <summary>
  /// Selects/highlights specific objects in Rhino.
  /// </summary>
  public async Task HighlightObjects(string[] objectIds) => await _basicConnectorBinding.HighlightObjects(objectIds);

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
