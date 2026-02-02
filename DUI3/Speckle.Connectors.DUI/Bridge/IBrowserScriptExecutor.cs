namespace Speckle.Connectors.DUI.Bridge;

public interface IBrowserScriptExecutor
{
  /// <summary>
  /// Fire and forget execution of the <paramref name="script"/>
  /// </summary>
  /// <remarks>
  /// Safe to call from any thread, will use the dispatcher only if called from not the main thread.
  ///
  /// Use <see cref="SendDispatched"/> if you wish to always use the dispatched (e.g. to keep UI responsive).
  /// </remarks>
  /// <exception cref="InvalidOperationException">thrown when <see cref="IsBrowserInitialized"/> is <see langword="false"/></exception>
  /// <param name="script">The (constant string) script to execute on the browser</param>
  void ExecuteScript(string script, CancellationToken cancellationToken);

  /// <summary>
  /// Defers execution of the <paramref name="script"/> to the Dispatcher
  ///
  /// Important: The behaviour of this function changes somewhat depending on whether you call it from the main thread.
  /// If you call it from the main thread, then it will ALSO allow the Dispatcher pump messages synchronously.
  /// Including messages other than <paramref name="script"/>
  ///
  /// This may be desirable (e.g. to keep UI responsive for progress reporting) but in other circumstances
  /// it may be dangerous to allow the dispatcher to fire other events.
  ///
  /// We've observed when calling <c>Dispatcher.Invoke(Action)</c> from within a Revit API event handler (main thread)
  /// that this causes Revit to fire other API events while we're still handling the previous one
  /// Leading to horrible undefined behaviour, unpredictable host app crashes, and overall headaches.
  ///
  /// So you must only call this function either from a non-main thread, or from the main thread if you actually want other parts Dispatcher system to process.
  /// </summary>
  /// <remarks>
  /// You should call this:
  ///  - To send "fire and forget" messages from non-main thread
  ///  - To send messages from main thread WHILE allowing the dispatcher to pump (e.g. to keep UI responsive) and is safe to do so.
  ///
  /// See <see cref="ExecuteScript"/> for the alternative.
  /// </remarks>
  /// <param name="script"></param>
  /// <param name="cancellationToken"></param>
  void SendDispatched(string script, CancellationToken cancellationToken);

  bool IsBrowserInitialized { get; }

  object BrowserElement { get; }

  /// <summary>
  /// Action that opens up the developer tools of the respective browser we're using. While webview2 allows for "right click, inspect", cefsharp does not - hence the need for this.
  /// </summary>
  void ShowDevTools();
}
