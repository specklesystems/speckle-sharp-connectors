using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.MicroStation.Plugin;

namespace Speckle.Connectors.MicroStation.Bindings;

/// <summary>
/// Reports the current selection to the DUI3 panel.
/// MicroStation 2026's COM API does not expose a selection-change event, so the
/// selection is captured on demand via <see cref="GetSelection"/> (called by the panel)
/// and polled whenever the idle timer fires.
/// </summary>
public class MicroStationSelectionBinding : ISelectionBinding
{
  private readonly IAppIdleManager _idleManager;
  private const string SELECTION_EVENT = "setSelection";

  public string Name { get; } = "selectionBinding";
  public IBrowserBridge Parent { get; }

  public MicroStationSelectionBinding(IAppIdleManager idleManager, IBrowserBridge parent)
  {
    _idleManager = idleManager;
    Parent = parent;

    // Schedule a periodic idle check to detect selection changes
    _idleManager.SubscribeToIdle(nameof(UpdateSelectionAsync), async () => await UpdateSelectionAsync());
  }

  private async Task UpdateSelectionAsync()
  {
    var selInfo = GetSelection();
    await Parent.Send(SELECTION_EVENT, selInfo);

    // Re-subscribe so selection is refreshed every idle cycle while panel is open
    _idleManager.SubscribeToIdle(nameof(UpdateSelectionAsync), async () => await UpdateSelectionAsync());
  }

  public SelectionInfo GetSelection()
  {
    var model = MsApp.ActiveModel;
    if (model == null || !model.AnyElementsSelected)
    {
      return new SelectionInfo([], "No selection");
    }

    // Iterate all graphic elements and collect those that are highlighted (= selected)
    var ids = new HashSet<string>();
    var enumerator = model.GraphicalElementCache.Scan(new MSIDGN.ElementScanCriteriaClass());
    while (enumerator.MoveNext())
    {
      var element = enumerator.Current;
      if (element?.IsHighlighted == true)
      {
        ids.Add(element.ID.ToString());
      }
    }

    return new SelectionInfo(ids, $"{ids.Count} object{(ids.Count != 1 ? "s" : "")}");
  }
}
