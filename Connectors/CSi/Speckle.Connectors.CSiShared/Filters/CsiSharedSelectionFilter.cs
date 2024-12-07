using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.CSiShared.Filters;

public class CsiSharedSelectionFilter : DirectSelectionSendFilter
{
  public CsiSharedSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds() => SelectedObjectIds;
}
