using Speckle.Connector.Navisworks.Services;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSelectionBinding : ISelectionBinding
{
  private readonly IElementSelectionService _selectionService;
  private readonly IEventAggregator _eventAggregator;
  private const string SELECTION_EVENT = "setSelection";
  public string Name { get; } = "selectionBinding";
  public IBrowserBridge Parent { get; }

  public NavisworksSelectionBinding(
    IBrowserBridge parent,
    IElementSelectionService selectionService,
    IEventAggregator eventAggregator
  )
  {
    _selectionService = selectionService;
    _eventAggregator = eventAggregator;
    Parent = parent;

    eventAggregator.GetEvent<SelectionChangedEvent>().Subscribe(OnSelectionChange);
    
  }

  private void OnSelectionChange(object _) =>
    _eventAggregator.GetEvent<IdleEvent>().OneTimeSubscribe(nameof(NavisworksSelectionBinding), UpdateSelectionAsync);

  private async Task UpdateSelectionAsync(object _)
  {
    var selInfo = GetSelection();
    await Parent.Send<SelectionInfo>(SELECTION_EVENT, selInfo);
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
        .CurrentSelection.SelectedItems.Where(_selectionService.IsVisible) // Exclude hidden elements
        .Select(_selectionService.GetModelItemPath) // Resolve to index paths
    );

    return new SelectionInfo(
      [.. selectedObjectsIds],
      $"{selectedObjectsIds.Count} object{(selectedObjectsIds.Count != 1 ? "s" : "")}"
    );
  }
}
