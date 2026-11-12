using Eto.Forms;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Settings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Newtonsoft.Json;
using IBinding = Speckle.Connectors.DUI.Bindings.IBinding;

namespace Speckle.Connectors.DUI.EtoWebView;

/// <summary>
/// Hosts the DUI in an Eto <see cref="WebView"/> and carries the bridge over WKWebView script messages (FEA-124).
/// </summary>
/// <remarks>
/// WebView2 injects each <see cref="IBrowserBridge"/> as a host object the page calls directly. Eto has no host objects,
/// so the DUI posts a JSON envelope through <c>window.eto.postMessage</c> and we dispatch it here; calls that return a
/// value are answered with <c>window.__speckleEto.resolve(callId, json)</c>. The .NET → JS leg is unchanged
/// (<see cref="IBrowserScriptExecutor.ExecuteScript"/>).
/// </remarks>
public sealed class DUI3EtoWebView : Panel, IBrowserScriptExecutor
{
  private readonly IServiceProvider _serviceProvider;
  private readonly WebView _browser = new();
  private readonly Dictionary<string, IEtoBindingBridge> _bridges = new(StringComparer.Ordinal);
  private readonly EtoMessageDispatcher _dispatcher;

  public DUI3EtoWebView(IServiceProvider serviceProvider, IGlobalConfigResolver globalConfigResolver)
  {
    _serviceProvider = serviceProvider;
    _dispatcher = new EtoMessageDispatcher(
      binding => _bridges.TryGetValue(binding, out var bridge) ? bridge : null,
      Resolve,
      ShowDevTools
    );
    _browser.MessageReceived += (_, e) =>
      _serviceProvider.GetRequiredService<ITopLevelExceptionHandler>().CatchUnhandled(() => OnMessage(e.Message));
    _browser.DocumentLoaded += (_, e) => RhinoApp.WriteLine($"Speckle: DUI loaded {e.Uri}");
    MakeInspectable();
    Content = _browser;
    // Bindings depend on IBrowserScriptExecutor (this object), so resolving them inside the ctor recurses through the
    // DI factory. Attach on the next main-loop tick — the same "after construction" timing as the WPF host's
    // CoreWebView2InitializationCompleted — and lazily on the first message as a belt-and-braces.
    Application.Instance.AsyncInvoke(() =>
      _serviceProvider.GetRequiredService<ITopLevelExceptionHandler>().CatchUnhandled(AttachBindings)
    );
    _browser.Url = globalConfigResolver.GetDuiUrl();
  }

  private int _firstMessageLogged;

  public bool IsBrowserInitialized { get; private set; }

  public object BrowserElement => _browser;

  public void ExecuteScript(string script, CancellationToken cancellationToken)
  {
    if (!RhinoApp.InvokeRequired)
    {
      _browser.ExecuteScriptAsync(script);
      return;
    }
    ExecuteScriptDispatched(script, cancellationToken);
  }

  public void ExecuteScriptDispatched(string script, CancellationToken cancellationToken) =>
    Application.Instance.AsyncInvoke(() =>
    {
      if (!cancellationToken.IsCancellationRequested)
      {
        _browser.ExecuteScriptAsync(script);
      }
    });

  public void ShowDevTools()
  {
    // no Eto API; Safari > Develop > <machine> > Rhinoceros exposes the WKWebView inspector
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      _browser.Dispose();
    }
    base.Dispose(disposing);
  }

  /// <summary>
  /// WKWebView.isInspectable (macOS 13.3+) so Safari's Develop menu can attach Web Inspector to the panel. Reached by
  /// reflection to avoid a Microsoft.macOS reference just for one property; absent on older OS = silently skipped.
  /// </summary>
  private void MakeInspectable()
  {
    var wk = _browser.ControlObject;
    wk?.GetType().GetProperty("Inspectable")?.SetValue(wk, true);
  }

  private void AttachBindings()
  {
    if (IsBrowserInitialized)
    {
      return;
    }
    foreach (var binding in _serviceProvider.GetRequiredService<IEnumerable<IBinding>>())
    {
      // A binding's bridge can only be associated once, so a retry after a partial failure skips the ones already done.
      if (_bridges.ContainsKey(binding.Name))
      {
        continue;
      }
      binding.Parent.AssociateWithBinding(binding);
      _bridges[binding.Name] = new BrowserBridgeEtoAdapter((BrowserBridge)binding.Parent);
    }
    IsBrowserInitialized = true;
  }

  private void OnMessage(string raw)
  {
    AttachBindings();
    if (Interlocked.Exchange(ref _firstMessageLogged, 1) == 0)
    {
      RhinoApp.WriteLine("Speckle: first DUI message received — Eto bridge is live");
    }
    _dispatcher.Dispatch(raw);
  }

  private void Resolve(string callId, string payload)
  {
    // JsonConvert.ToString yields a JS-safe quoted literal, so payload survives quotes, slashes and newlines intact
    ExecuteScript(
      $"window.__speckleEto.resolve({JsonConvert.ToString(callId)}, {JsonConvert.ToString(payload)})",
      CancellationToken.None
    );
  }
}
