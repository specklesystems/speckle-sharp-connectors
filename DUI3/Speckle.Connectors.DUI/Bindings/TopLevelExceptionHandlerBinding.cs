using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// Simple binding that can be injected into non-<see cref="IBinding"/> services to get access to the <see cref="IBrowserBridge.TopLevelExceptionHandler"/>
/// </summary>
/// <param name="parent"></param>
public sealed class TopLevelExceptionHandlerBinding(IBrowserBridge parent) : IBinding
{
  public string Name => "topLevelExceptionHandlerBinding";
  public IBrowserBridge Parent => parent;
}
