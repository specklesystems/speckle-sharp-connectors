using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.Autocad.Filters;

public class AutocadSelectionFilter : DirectSelectionSendFilter
{
  public AutocadSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> GetObjectIds() => SelectedObjectIds;
}
