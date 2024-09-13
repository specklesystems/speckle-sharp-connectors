using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.WebView;

public sealed partial class DUI3ControlWebView : UserControl, IBrowserScriptExecutor
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

  // {
  //   if (!Browser.IsInitialized)
  //   {
  //     throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet.");
  //   }
  //
  //   var t = Browser.Dispatcher.Invoke(
  //     async () =>
  //     {
  //       var res = await Browser.ExecuteScriptAsync(script).ConfigureAwait(true);
  //       await Task.Delay(100).ConfigureAwait(true);
  //       return res;
  //     },
  //     DispatcherPriority.Background
  //   );
  //
  //   _ = t.IsCompleted;

  // bool isAlreadyMainThread = Browser.Dispatcher.CheckAccess();
  // if (isAlreadyMainThread)
  // {
  //   Browser.ExecuteScriptAsync(script);
  // }
  // else
  // {
  //   Browser.Dispatcher.Invoke(
  //     () =>
  //     {
  //       return Browser.ExecuteScriptAsync(script);
  //     },
  //     DispatcherPriority.Background
  //   );
  // }
  // }

  public async Task ExecuteScriptAsyncMethod(string script, CancellationToken cancellationToken)
  {
    if (!Browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet.");
    }

    var callbackTask = await Browser
      .Dispatcher.InvokeAsync(
        async () => await Browser.ExecuteScriptAsync(script).ConfigureAwait(false),
        DispatcherPriority.Background,
        cancellationToken
      )
      .Task.ConfigureAwait(false);

    _ = await callbackTask.ConfigureAwait(false);
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

  /// <remark>
  /// This must be called on the Main thread
  /// </remark>
  private void SetupBinding(IBinding binding)
  {
    binding.Parent.AssociateWithBinding(binding);
    Browser.CoreWebView2.AddHostObjectToScript(binding.Name, binding.Parent);
  }

  public void ShowDevTools() => Browser.CoreWebView2.OpenDevToolsWindow();
}
