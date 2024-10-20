using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.Revit.Bindings;

internal abstract class RevitBaseBinding : IBinding
{
  // POC: name and bridge might be better for them to be protected props?
  public string Name { get; }
  public IBrowserBridge Parent { get; }

  protected readonly DocumentModelStore Store;
  protected readonly RevitContext RevitContext;

  protected RevitBaseBinding(string name, DocumentModelStore store, IBrowserBridge parent, RevitContext revitContext)
  {
    Name = name;
    Parent = parent;
    Store = store;
    RevitContext = revitContext;
  }
}
