using Speckle.Connector.Navisworks.Extensions;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSelectionBinding : ISelectionBinding
{
  private readonly IAppIdleManager _appIdleManager;
  private const string SELECTION_EVENT = "setSelection";
  public string Name { get; } = "selectionBinding";
  public IBrowserBridge Parent { get; }

  public NavisworksSelectionBinding(IAppIdleManager idleManager, IBrowserBridge parent)
  {
    _appIdleManager = idleManager;
    Parent = parent;

    NavisworksApp.ActiveDocument.CurrentSelection.Changed += OnSelectionChange;
  }

  private void OnSelectionChange(object? o, EventArgs eventArgs) =>
    _appIdleManager.SubscribeToIdle(
      nameof(NavisworksSelectionBinding),
      async () => await UpdateSelectionAsync().ConfigureAwait(false)
    );

  private void UpdateSelection()
  {
    SelectionInfo selInfo = GetSelection();
    Parent.Send(SELECTION_EVENT, selInfo);
  }

  private async Task UpdateSelectionAsync()
  {
    var selInfo = await Parent
      .RunOnMainThreadAsync<SelectionInfo>(() => Task.FromResult(GetSelection()))
      .ConfigureAwait(false);

    await Parent.Send<SelectionInfo>(SELECTION_EVENT, selInfo).ConfigureAwait(false);
  }

  public SelectionInfo GetSelection()
  {
    // Ensure there is an active document and a valid selection
    var activeDocument = NavisworksApp.ActiveDocument;
    if (activeDocument == null || activeDocument.CurrentSelection.SelectedItems.IsEmpty)
    {
      // Return an empty list if no valid selection exists
      return new SelectionInfo([], "No selection available");
    }

    // Ensure only visible elements are processed by filtering using IsElementVisible
    var selectedObjectsIds = new HashSet<string>(
      activeDocument
        .CurrentSelection.SelectedItems.Where(ElementSelectionExtension.IsElementVisible) // Exclude hidden elements
        .Select(ElementSelectionExtension.ResolveModelItemToIndexPath) // Resolve to index paths
    );

    return new SelectionInfo(
      [.. selectedObjectsIds],
      $"{selectedObjectsIds.Count} object{(selectedObjectsIds.Count != 1 ? "s" : "")}"
    );
  }
}
