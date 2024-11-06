using Speckle.Connectors.DUI.Models.Card.SendFilter;

namespace Speckle.Connector.Tekla2024.Filters;

public class TeklaSelectionFilter : DirectSelectionSendFilter
{
  public TeklaSelectionFilter()
  {
    IsDefault = true;
  }

  public override List<string> SetObjectIds() => ObjectIds;
}
