using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.MicroStation.Plugin;

namespace Speckle.Connectors.MicroStation.Operations.Send.Filters;

/// <summary>
/// Sends only the elements currently selected (highlighted) in the MicroStation selection set.
/// The selection is captured at the time the model card is created or refreshed.
/// </summary>
public class MicroStationSelectionFilter : DirectSelectionSendFilter
{
  public MicroStationSelectionFilter()
  {
    IsDefault = false;
  }

  public override List<string> RefreshObjectIds()
  {
    var model = MsApp.ActiveModel;
    if (model == null || !model.AnyElementsSelected)
    {
      return SelectedObjectIds;
    }

    // Collect elements flagged as IsHighlighted (= currently selected)
    var ids = new List<string>();
    var enumerator = model.GraphicalElementCache.Scan(new MSIDGN.ElementScanCriteriaClass());
    while (enumerator.MoveNext())
    {
      var element = enumerator.Current;
      if (element?.IsHighlighted == true)
      {
        ids.Add(element.ID.ToString());
      }
    }

    SelectedObjectIds = ids;
    return SelectedObjectIds;
  }
}
