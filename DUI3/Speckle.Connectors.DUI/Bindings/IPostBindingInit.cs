using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.Bindings;

public interface IPostInitBinding : IBinding
{
  /// <summary>
  /// Callback function called as the final step of <see cref="IBrowserBridge.AssociateWithBinding"/> when the bridge is fully operational.
  /// Until this function is called, any use of the <see cref="IBrowserBridge"/> is unsafe!
  /// </summary>
  /// <remarks>
  /// Any logic in an <see cref="IBinding"/>'s constructor that leads to the use of the <see cref="IBrowserBridge"/> is unsafe!
  /// because the <see cref="IBrowserBridge"/> is not fully operational until after the <see cref="IBrowserBridge.AssociateWithBinding"/> has run.<br/>
  /// This callback function should be used instead of a constructor to register any event listeners, use the <see cref="ITopLevelExceptionHandler"/>, or do anything that actually "starts" external behaviour of an <see cref="IBinding"/>
  /// </remarks>
  void PostInitialization();
}
