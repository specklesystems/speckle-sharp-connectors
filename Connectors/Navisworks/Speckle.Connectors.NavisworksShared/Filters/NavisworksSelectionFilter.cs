using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connector.Navisworks.Filters;

public class NavisworksSelectionFilter : DirectSelectionSendFilter
{
  public NavisworksSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => [];
}
