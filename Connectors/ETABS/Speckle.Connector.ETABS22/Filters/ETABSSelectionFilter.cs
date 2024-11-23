using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connector.ETABS22.Filters;

public class ETABSSelectionFilter : DirectSelectionSendFilter
{
  public ETABSSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => SelectedObjectIds;
}
