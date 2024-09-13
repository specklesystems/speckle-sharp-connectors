using Speckle.Connectors.DUI.Bindings;

namespace Speckle.Connectors.DUI.Bridge;

/// <summary>
/// Describes a bridge - a wrapper class around a specific browser host. Not needed right now,
/// but if in the future we will have other bridge classes (e.g, ones that wrap around other browsers),
/// it just might be useful.
/// </summary>
public interface IBridge
{
  // POC: documnetation comments
  string FrontendBoundName { get; }

  void AssociateWithBinding(IBinding binding);

  /// <summary>
  /// This method is called by the Frontend bridge to understand what it can actually call. It should return the method names of the bindings that this bridge wraps around.
  /// </summary>
  /// <returns></returns>
  public string[] GetBindingsMethodNames();

  /// <summary>
  /// This method is called by the Frontend bridge when invoking any of the wrapped binding's methods.
  /// </summary>
  /// <param name="methodName"></param>
  /// <param name="requestId"></param>
  /// <param name="args"></param>
  /// <returns></returns>
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
  /// <seealso cref="RunOnMainThreadAsync"/>
  /// <seealso cref="RunOnMainThreadAsync{T}"/>
  public void RunOnMainThread(Action action);

  /// <summary>
  /// <inheritdoc cref="RunOnMainThread"/>
  /// </summary>
  /// <returns>An awaitable <see cref="Task"/></returns>
  /// <param name="action"><inheritdoc cref="RunOnMainThread"/></param>
  /// <seealso cref="RunOnMainThread"/>
  /// <seealso cref="RunOnMainThreadAsync{T}"/>
  public Task RunOnMainThreadAsync(Action action);

  /// <summary>
  /// <inheritdoc cref="RunOnMainThread"/>
  /// </summary>
  /// <returns>An awaitable <see cref="Task{T}"/></returns>
  /// <param name="action"><inheritdoc cref="RunOnMainThread"/></param>
  /// <seealso cref="RunOnMainThread"/>
  /// <seealso cref="RunOnMainThreadAsync"/>
  public Task<T> RunOnMainThreadAsync<T>(Func<T> action);

  /// <param name="eventName"></param>
  /// <exception cref="InvalidOperationException">Bridge was not initialized with a binding</exception>
  public Task Send(string eventName, CancellationToken cancellationToken = default);

  /// <inheritdoc cref="Send(string, CancellationToken)"/>
  /// <param name="data">data to store</param>
  /// <typeparam name="T"></typeparam>
  /// <exception cref="InvalidOperationException">Bridge was not initialized with a binding</exception>
  public Task Send<T>(string eventName, T data, CancellationToken cancellationToken = default)
    where T : class;

  public ITopLevelExceptionHandler TopLevelExceptionHandler { get; }
}
