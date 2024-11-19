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
    _appIdleManager.SubscribeToIdle(nameof(NavisworksSelectionBinding), UpdateSelection);

  private void UpdateSelection()
  {
    SelectionInfo selInfo = GetSelection();
    Parent.Send(SELECTION_EVENT, selInfo);
  }

  public SelectionInfo GetSelection()
  {
    return new SelectionInfo([], "No selection available");
  }
}
