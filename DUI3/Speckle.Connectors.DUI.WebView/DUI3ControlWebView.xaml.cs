using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.WebView;

public sealed partial class DUI3ControlWebView : UserControl, IBrowserScriptExecutor, IDisposable
{
  private readonly IServiceProvider _serviceProvider;

  public DUI3ControlWebView(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
    InitializeComponent();

    Browser.CoreWebView2InitializationCompleted += (sender, args) =>
      _serviceProvider
        .GetRequiredService<ITopLevelExceptionHandler>()
        .CatchUnhandled(() => OnInitialized(sender, args));
  }

  public bool IsBrowserInitialized => Browser.IsInitialized;

  public object BrowserElement => Browser;

  public void ExecuteScript(string script)
  {
    if (!Browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet.");
    }

    try
    {
      //always invoke even on the main thread because it's better somehow
      Browser.Dispatcher.Invoke(
        //fire and forget
        () => Browser.ExecuteScriptAsync(script),
        DispatcherPriority.Background
      );
    }
    catch (OperationCanceledException)
    {
      //do nothing, happens when closing Revit while a script is being executed
    }
  }

  public Task ExecuteScriptAsyncMethod(string script, CancellationToken cancellationToken)
  {
    if (!Browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet.");
    }
    //always invoke even on the main thread because it's better somehow
    Browser.Dispatcher.Invoke(
      //fire and forget
      () => Browser.ExecuteScriptAsync(script),
      DispatcherPriority.Background
    );
    return Task.CompletedTask;
  }

  public void SendProgress(string script) => ExecuteScript(script);

  private void OnInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
  {
    if (!e.IsSuccess)
    {
      throw new InvalidOperationException("Webview Failed to initialize", e.InitializationException);
    }

    // We use Lazy here to delay creating the binding until after the Browser is fully initialized.
    // Otherwise the Browser cannot respond to any requests to ExecuteScriptAsyncMethod
    foreach (var binding in _serviceProvider.GetRequiredService<IEnumerable<IBinding>>())
    {
      SetupBinding(binding);
    }
  }

  /// <remark>
  /// This must be called on the Main thread
  /// </remark>
  private void SetupBinding(IBinding binding)
  {
    binding.Parent.AssociateWithBinding(binding);
    Browser.CoreWebView2.AddHostObjectToScript(binding.Name, binding.Parent);
  }

  public void ShowDevTools() => Browser.CoreWebView2.OpenDevToolsWindow();

  //https://github.com/MicrosoftEdge/WebView2Feedback/issues/2161
  public void Dispose() => Browser.Dispatcher.Invoke(() => Browser.Dispose(), DispatcherPriority.Send);
}
