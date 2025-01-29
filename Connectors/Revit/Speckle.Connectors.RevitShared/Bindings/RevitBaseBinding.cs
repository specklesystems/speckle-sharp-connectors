using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Revit.Bindings;

internal abstract class RevitBaseBinding : IBinding
{
  // POC: name and bridge might be better for them to be protected props?
  public string Name { get; }
  public IBrowserBridge Parent { get; }

  protected RevitBaseBinding(string name, IBrowserBridge parent)
  {
    Name = name;
    Parent = parent;
  }
}
