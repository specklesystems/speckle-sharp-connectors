using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Revit.Plugin;

namespace Speckle.Connectors.Revit2026.Plugin;

public sealed partial class RevitControlWebView : UserControl, IBrowserScriptExecutor, IDisposable
{
  private readonly IServiceProvider _serviceProvider;
  private readonly IRevitTask _revitTask;
#pragma warning disable CA2213
  private WebView2? _browser;
#pragma warning restore CA2213
  private bool _isInitializing;

  public RevitControlWebView(IServiceProvider serviceProvider, IRevitTask revitTask)
  {
    _serviceProvider = serviceProvider;
    _revitTask = revitTask;
    InitializeComponent();

    // Delay WebView2 creation until the panel is actually visible
    // This avoids conflicts with other plugins (like pyRevit) during startup
    IsVisibleChanged += OnIsVisibleChanged;
  }

  private void OnIsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
  {
    if (e.NewValue is true && _browser == null && !_isInitializing)
    {
      _isInitializing = true;
      Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, CreateWebView2);
    }
  }

  private void CreateWebView2()
  {
    _browser = new WebView2
    {
      CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = "C:\\temp" },
      HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
      VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
      Source = Url.Netlify
    };

    _browser.CoreWebView2InitializationCompleted += (sender, args) =>
      _serviceProvider
        .GetRequiredService<ITopLevelExceptionHandler>()
        .CatchUnhandled(() => OnInitialized(sender, args));

    BrowserContainer.Child = _browser;
  }

  public bool IsBrowserInitialized => _browser?.IsInitialized ?? false;

  public object BrowserElement => _browser!;

  /// <inheritdoc/>
  public void ExecuteScript(string script, CancellationToken cancellationToken)
  {
    if (_browser == null || !_browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet");
    }

    if (!_browser.CheckAccess())
    {
      SendDispatched(script, cancellationToken);
      return;
    }

    _browser.ExecuteScriptAsync(script);
  }

  /// <inheritdoc/>
  public void ExecuteScriptDispatched(string script, CancellationToken cancellationToken)
  {
    if (_browser == null || !_browser.IsInitialized)
    {
      throw new InvalidOperationException("Failed to execute script, Webview2 is not initialized yet");
    }

    //Intentionally using the dispatcher even from the main thread
    //As it allows the UI to pump messages, and stay responsive
    _browser.Dispatcher.Invoke(
      () => _browser.ExecuteScriptAsync(script),
      DispatcherPriority.Background,
      cancellationToken
    );
  }

  private void OnInitialized(object? sender, CoreWebView2InitializationCompletedEventArgs e)
  {
    Console.WriteLine(CoreWebView2Environment.GetAvailableBrowserVersionString());
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
    _browser!.CoreWebView2.AddHostObjectToScript(binding.Name, binding.Parent);
  }

  public void ShowDevTools() => _browser?.CoreWebView2?.OpenDevToolsWindow();

  //https://github.com/MicrosoftEdge/WebView2Feedback/issues/2161
  public void Dispose()
  {
    if (_browser != null)
    {
      _browser.Dispatcher.Invoke(() => _browser.Dispose(), DispatcherPriority.Send);
      _browser = null;
    }
  }
}
