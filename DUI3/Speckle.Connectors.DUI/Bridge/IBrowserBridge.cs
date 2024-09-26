using Speckle.Connectors.DUI.Bindings;

namespace Speckle.Connectors.DUI.Bridge;

/// <summary>
/// Describes a bridge - a wrapper class around a specific browser host. Not needed right now,
/// but if in the future we will have other bridge classes (e.g, ones that wrap around other browsers),
/// it just might be useful.
/// </summary>
public interface IBrowserBridge
{
  /// <summary>
  /// The name under which we expect the frontend to hoist this bindings class to the global scope.
  /// e.g., `receiveBindings` should be available as `window.receiveBindings`.
  /// </summary>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  string FrontendBoundName { get; }

  public ITopLevelExceptionHandler TopLevelExceptionHandler { get; }

  void AssociateWithBinding(IBinding binding);

  /// <summary>
  /// Returns the method names of the binding that this bridge wraps around.
  /// </summary>
  /// <remarks>This method is called by the Frontend bridge to understand what it can actually call</remarks>
  /// <returns></returns>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  public string[] GetBindingsMethodNames();

  /// <summary>
  /// This method is called by the Frontend bridge when invoking any of the wrapped binding's methods.
  /// </summary>
  /// <param name="methodName">The name of the .NET function to invoke</param>
  /// <param name="requestId">A unique Id for this request</param>
  /// <param name="args">A JSON array of args to deserialize and use to invoke the method</param>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  public void RunMethod(string methodName, string requestId, string args);

  /// <summary>
  /// Posts an <paramref name="action"/> onto the main thread
  /// Some applications might need to run some operations on main thread as deferred actions.
  /// </summary>
  /// <remarks>
  /// This function returns immediately; fire and forget.<br/>
  /// Exceptions will be caught by <see cref="TopLevelExceptionHandler"/>
  /// </remarks>
  /// <param name="action"> Action to run on the main thread</param>
  public void RunOnMainThread(Action action);

  /// <param name="eventName"></param>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  public void Send(string eventName);

  /// <inheritdoc cref="Send(string)"/>
  /// <param name="data">data to store</param>
  /// <typeparam name="T"></typeparam>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  public void Send<T>(string eventName, T data)
    where T : class;

  /// <exception cref="InvalidOperationException">The <see cref="IBrowserBridge"/> was not initialized with an <see cref="IBinding"/> (see <see cref="AssociateWithBinding"/>)</exception>
  public void AssertBindingInitialised();
}
