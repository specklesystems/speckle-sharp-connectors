namespace Speckle.Connectors.DUI.Bridge;

public interface IBrowserScriptExecutor
{
  /// <exception cref="InvalidOperationException">thrown when <see cref="IsBrowserInitialized"/> is <see langword="false"/></exception>
  /// <param name="script">The (constant string) script to execute on the browser</param>
  public Task ExecuteScriptAsyncMethod(string script, CancellationToken cancellationToken);

  public bool IsBrowserInitialized { get; }

  public object BrowserElement { get; }

  /// <summary>
  /// Action that opens up the developer tools of the respective browser we're using. While webview2 allows for "right click, inspect", cefsharp does not - hence the need for this.
  /// </summary>
  public void ShowDevTools();
}
