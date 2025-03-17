using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.TeklaShared.Filters;

public class TeklaSelectionFilter : DirectSelectionSendFilter
{
  public TeklaSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => SelectedObjectIds;
}
