using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.WebView;

public sealed partial class DUI3ControlWebView : UserControl, IBrowserScriptExecutor, IDisposable
{
  private readonly IEnumerable<Lazy<IBinding>> _bindings;

  public DUI3ControlWebView(
    IEnumerable<Lazy<IBinding>> bindings,
    Lazy<ITopLevelExceptionHandler> topLevelExceptionHandler
  )
  {
    _bindings = bindings;
    InitializeComponent();

    Browser.CoreWebView2InitializationCompleted += (sender, args) =>
      topLevelExceptionHandler.Value.CatchUnhandled(() => OnInitialized(sender, args));
  }

  public bool IsBrowserInitialized => Browser.IsInitialized;

  public object BrowserElement => Browser;

  public void ExecuteScriptAsyncMethod(string script)
  {
    if (!Browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet.");
    }

    Browser.Dispatcher.Invoke(() => Browser.ExecuteScriptAsync(script), DispatcherPriority.Background);
  }

  private void OnInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
  {
    if (!e.IsSuccess)
    {
      throw new InvalidOperationException("Webview Failed to initialize", e.InitializationException);
    }

    // We use Lazy here to delay creating the binding until after the Browser is fully initialized.
    // Otherwise the Browser cannot respond to any requests to ExecuteScriptAsyncMethod
    foreach (Lazy<IBinding> lazyBinding in _bindings)
    {
      SetupBinding(lazyBinding.Value);
    }
  }

  private void SetupBinding(IBinding binding)
  {
    binding.Parent.AssociateWithBinding(binding);
    Browser.CoreWebView2.AddHostObjectToScript(binding.Name, binding.Parent);
  }

  public void ShowDevTools() => Browser.CoreWebView2.OpenDevToolsWindow();

  //https://github.com/MicrosoftEdge/WebView2Feedback/issues/2161
  public void Dispose() => Browser.Dispatcher.Invoke(() => Browser.Dispose(), DispatcherPriority.Send);
}
