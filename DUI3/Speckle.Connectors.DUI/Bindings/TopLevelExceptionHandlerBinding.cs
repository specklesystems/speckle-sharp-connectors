using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

/// <summary>
/// Simple binding that can be injected into non-<see cref="IBinding"/> services to get access to the <see cref="IBrowserBridge.TopLevelExceptionHandler"/>
/// </summary>
/// <remarks>
/// This binding only exists inorder for the <see cref="ITopLevelExceptionHandler"/> to beable to communicate witht the <see cref="IBrowserBridge"/>.
/// The <see cref="ITopLevelExceptionHandler"/> of this binding can be injected into other services without even knowing its bound to this <see cref="TopLevelExceptionHandlerBinding"/>
/// This <see cref="ITopLevelExceptionHandler"/> is setup with <see cref="ITopLevelExceptionHandler.AllowUseWithoutBrowser"/> <see langword="false"/>
/// allow for use before the <see cref="IBrowserBridge"/> is fully initialised</remarks>
public sealed class TopLevelExceptionHandlerBinding : IBinding
{
  public string Name => "topLevelExceptionHandlerBinding";
  public IBrowserBridge Parent { get; }

  public TopLevelExceptionHandlerBinding(IBrowserBridge parent)
  {
    Parent = parent;
    Parent.TopLevelExceptionHandler.AllowUseWithoutBrowser = true; //Allows use of injected ITopLevelExceptionHandler s before the browser is injected, this should really be the only place we set this to be true
  }
}
