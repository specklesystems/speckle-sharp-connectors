using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

public sealed class TopLevelExceptionHandlerBinding(IBridge parent) : IBinding
{
  public string Name => "topLevelExceptionHandlerBinding";
  public IBridge Parent => parent;
}
