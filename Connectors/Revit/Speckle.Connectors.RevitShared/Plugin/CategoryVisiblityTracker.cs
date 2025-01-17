using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.Plugin;

public class CategoryVisibilityTracker
{
  private readonly Document _doc;
  private readonly Dictionary<ElementId, bool> _visibilityStates;

  public CategoryVisibilityTracker(Document doc)
  {
    _doc = doc;
    _visibilityStates = new Dictionary<ElementId, bool>();
    InitializeVisibilityStates();
  }

  private void InitializeVisibilityStates()
  {
    foreach (Category category in _doc.Settings.Categories)
    {
      if (category.get_AllowsVisibilityControl(_doc.ActiveView))
      {
        _visibilityStates[category.Id] = category.get_Visible(_doc.ActiveView);
      }
    }
  }

  public bool HasVisibilityChanged(Category category)
  {
    if (_visibilityStates.TryGetValue(category.Id, out bool previousState))
    {
      bool currentState = category.get_Visible(_doc.ActiveView);
      if (currentState != previousState)
      {
        _visibilityStates[category.Id] = currentState; // Update the state
        return true;
      }
    }
    return false;
  }
}
