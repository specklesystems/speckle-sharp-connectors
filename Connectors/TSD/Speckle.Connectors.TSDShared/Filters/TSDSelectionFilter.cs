using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.TSDShared.Filters;

internal sealed class TSDSelectionFilter : DirectSelectionSendFilter
{
  public TSDSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => SelectedObjectIds;
}
