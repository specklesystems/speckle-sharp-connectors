using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.ArcGIS.Filters;

public class ArcGISSelectionFilter : DirectSelectionSendFilter
{
  public ArcGISSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> GetObjectIds() => SelectedObjectIds;

  public override bool CheckExpiry(string[] changedObjectIds) => SelectedObjectIds.Intersect(changedObjectIds).Any();
}
