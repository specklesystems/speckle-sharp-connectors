using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.MicroStation.Plugin;

namespace Speckle.Connectors.MicroStation.Bindings;

/// <summary>
/// Reports the current selection to the DUI3 panel. MicroStation 2026's COM API does not expose
/// a selection-change event, so the binding polls on the WPF idle timer (~100 ms cadence via
/// <c>MicroStationIdleManager</c>). To avoid flooding the bridge with redundant updates we hash
/// the selected-id set on each tick and only emit <c>setSelection</c> when it changes.
/// </summary>
public class MicroStationSelectionBinding : ISelectionBinding
{
  private readonly IAppIdleManager _idleManager;
  private const string SELECTION_EVENT = "setSelection";
  private const string SUBSCRIPTION_ID = nameof(UpdateSelectionAsync);

  // Hash of the last set of selected IDs we pushed to the bridge. Empty string represents
  // "no selection has ever been pushed yet" — first idle tick will always emit so the panel
  // gets the initial state on connector startup.
  private string _lastSelectionHash = string.Empty;

  public string Name { get; } = "selectionBinding";
  public IBrowserBridge Parent { get; }

  public MicroStationSelectionBinding(IAppIdleManager idleManager, IBrowserBridge parent)
  {
    _idleManager = idleManager;
    Parent = parent;

    // Schedule the initial idle check; the handler re-subscribes itself for continuous polling.
    _idleManager.SubscribeToIdle(SUBSCRIPTION_ID, async () => await UpdateSelectionAsync());
  }

  private async Task UpdateSelectionAsync()
  {
    try
    {
      var selInfo = GetSelection();
      var hash = ComputeIdHash(selInfo.SelectedObjectIds);

      if (hash != _lastSelectionHash)
      {
        _lastSelectionHash = hash;
        await Parent.Send(SELECTION_EVENT, selInfo);
      }
    }
    finally
    {
      // Re-subscribe regardless of success — IAppIdleManager subscriptions are one-shot.
      // Without this the polling stops after the first tick.
      _idleManager.SubscribeToIdle(SUBSCRIPTION_ID, async () => await UpdateSelectionAsync());
    }
  }

  public SelectionInfo GetSelection()
  {
    var model = MsApp.ActiveModel;
    if (model == null || !model.AnyElementsSelected)
    {
      return new SelectionInfo([], "No selection");
    }

    var ids = new HashSet<string>();

    // Prefer the targeted GetSelectedElements() API — only iterates currently-selected elements
    // rather than scanning the entire model cache. Falls back to a full scan with IsHighlighted
    // check on the off chance GetSelectedElements throws (some COM edge cases on transient state).
    try
    {
      var selected = model.GetSelectedElements();
      while (selected.MoveNext())
      {
        var element = selected.Current;
        if (element != null)
        {
          ids.Add(element.ID.ToString());
        }
      }
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      ids.Clear();
      var enumerator = model.GraphicalElementCache.Scan(new MSIDGN.ElementScanCriteriaClass());
      while (enumerator.MoveNext())
      {
        var element = enumerator.Current;
        if (element?.IsHighlighted == true)
        {
          ids.Add(element.ID.ToString());
        }
      }
    }

    return new SelectionInfo(ids, $"{ids.Count} object{(ids.Count != 1 ? "s" : "")}");
  }

  // Order-independent hash so {1,2,3} and {3,2,1} produce the same fingerprint.
  // Sorting the small id set on each tick is fine; selection sets are typically small.
  private static string ComputeIdHash(IReadOnlyCollection<string> ids)
  {
    if (ids.Count == 0)
    {
      return "<empty>";
    }
    var sorted = ids.ToArray();
    Array.Sort(sorted, StringComparer.Ordinal);
    return string.Join(",", sorted);
  }
}
