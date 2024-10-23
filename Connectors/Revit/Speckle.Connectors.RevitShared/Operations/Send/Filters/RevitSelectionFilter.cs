using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitSelectionFilter : DirectSelectionSendFilter
{
  public override List<string> GetObjectIds() => SelectedObjectIds;

  public override bool CheckExpiry(string[] changedObjectIds) => SelectedObjectIds.Intersect(changedObjectIds).Any();
}
