using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.Rhino.Filters;

public class RhinoSelectionFilter : DirectSelectionSendFilter
{
  public RhinoSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds()
  {
    SelectedObjectIds = SelectedObjectIds; // We know it is bad, it is for backward compatibility!
    return SelectedObjectIds;
  }
}
