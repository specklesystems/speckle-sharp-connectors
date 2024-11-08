using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitSelectionFilter : DirectSelectionSendFilter
{
  public RevitSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> RefreshObjectIds()
  {
    ObjectIds = SelectedObjectIds; // We know it is bad, it is for backward compatibility!
    return ObjectIds;
  }
}
