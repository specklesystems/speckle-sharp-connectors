using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.CSiShared.Filters;

public class CSiSharedSelectionFilter : DirectSelectionSendFilter
{
  public CSiSharedSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => SelectedObjectIds;
}
