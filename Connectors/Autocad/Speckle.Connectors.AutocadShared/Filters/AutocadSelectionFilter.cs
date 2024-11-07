using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.Autocad.Filters;

public class AutocadSelectionFilter : DirectSelectionSendFilter
{
  public AutocadSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> SetObjectIds()
  {
    ObjectIds = SelectedObjectIds; // We know it is bad, it is for backward compatibility!
    return ObjectIds;
  }
}
