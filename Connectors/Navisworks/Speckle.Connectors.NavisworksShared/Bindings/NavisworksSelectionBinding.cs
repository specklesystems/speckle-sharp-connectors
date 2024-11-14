using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.Navisworks.Bindings;

public class NavisworksSelectionBinding : ISelectionBinding
{
  public string Name { get; } = "selectionBinding";
  public IBrowserBridge Parent { get; }

  public SelectionInfo GetSelection() => new([], "No selection available");
}
