using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connector.Tekla2024.Filters;

public class TeklaSelectionFilter : DirectSelectionSendFilter
{
  public override List<string> GetObjectIds() => SelectedObjectIds;

  public override bool CheckExpiry(string[] changedObjectIds) => SelectedObjectIds.Intersect(changedObjectIds).Any();
}
