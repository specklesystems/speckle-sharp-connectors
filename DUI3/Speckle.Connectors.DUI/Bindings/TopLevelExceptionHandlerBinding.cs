using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

public sealed class TopLevelExceptionHandlerBinding(IBrowserBridge parent) : IBinding
{
  public string Name => "topLevelExceptionHandlerBinding";
  public IBrowserBridge Parent => parent;
}
