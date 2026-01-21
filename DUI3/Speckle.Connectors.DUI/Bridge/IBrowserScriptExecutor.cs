namespace Speckle.Connectors.DUI.Bridge;

public interface IBrowserScriptExecutor
{
  /// <exception cref="InvalidOperationException">thrown when <see cref="IsBrowserInitialized"/> is <see langword="false"/></exception>
  /// <param name="script">The (constant string) script to execute on the browser</param>
  void ExecuteScript(string script);

  public Task ExecuteScriptAsyncMethod(string script, CancellationToken cancellationToken);

  void SendProgress(string script);

  bool IsBrowserInitialized { get; }

  object BrowserElement { get; }

  /// <summary>
  /// Action that opens up the developer tools of the respective browser we're using. While webview2 allows for "right click, inspect", cefsharp does not - hence the need for this.
  /// </summary>
  void ShowDevTools();
}
