using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connectors.ArcGIS.Filters;

public class ArcGISSelectionFilter : DirectSelectionSendFilter
{
  public ArcGISSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> SetObjectIds()
  {
    ObjectIds = SelectedObjectIds; // We know it is bad, it is for backward compatibility!
    return ObjectIds;
  }
}
