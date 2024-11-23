using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connector.ETABS22.Bindings;

public class ETABSSelectionBinding : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public ETABSSelectionBinding(IBrowserBridge parent)
  {
    Parent = parent;
  }

  public SelectionInfo GetSelection()
  {
    // placeholder for actual implementation
    return new SelectionInfo(new List<string>(), "No objects selected.");
  }
}
