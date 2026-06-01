using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.TSDShared.Bindings;

internal sealed class TSDSelectionBinding : ISelectionBinding
{
  public string Name => "selectionBinding";
  public IBrowserBridge Parent { get; }

  public TSDSelectionBinding(IBrowserBridge parent)
  {
    Parent = parent;
  }

  public SelectionInfo GetSelection() => new([], "No objects selected.");
}
