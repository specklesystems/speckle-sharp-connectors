using Eto.Forms;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Settings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Newtonsoft.Json;
using IBinding = Speckle.Connectors.DUI.Bindings.IBinding;

namespace Speckle.Connectors.DUI.EtoWebView;

/// <summary>
/// Hosts the DUI in an Eto <see cref="WebView"/> and carries the bridge over WKWebView script messages
/// (rhino-mac-connector spec, ADR-0001).
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
  private readonly Dictionary<string, BrowserBridge> _bridges = new(StringComparer.Ordinal);

  public DUI3EtoWebView(IServiceProvider serviceProvider, IGlobalConfigResolver globalConfigResolver)
  {
    _serviceProvider = serviceProvider;
    _browser.MessageReceived += (_, e) =>
      _serviceProvider.GetRequiredService<ITopLevelExceptionHandler>().CatchUnhandled(() => OnMessage(e.Message));
    _browser.DocumentLoaded += (_, e) => RhinoApp.WriteLine($"Speckle: DUI loaded {e.Uri}");
    MakeInspectable();
    Content = _browser;
    // Bindings depend on IBrowserScriptExecutor (this object), so resolving them inside the ctor recurses through the
    // DI factory. Attach on the next main-loop tick — the same "after construction" timing as the WPF host's
    // CoreWebView2InitializationCompleted — and lazily on the first message as a belt-and-braces.
    Application.Instance.AsyncInvoke(AttachBindings);
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
    Application.Instance.AsyncInvoke(() => _browser.ExecuteScriptAsync(script));

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
      binding.Parent.AssociateWithBinding(binding);
      // GetCallResult/OpenUrl live on the concrete bridge (they are the host-object surface, not IBrowserBridge)
      _bridges[binding.Name] = (BrowserBridge)binding.Parent;
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
    var message = JsonConvert.DeserializeObject<EtoMessage>(raw);
    if (message?.Binding is null || !_bridges.TryGetValue(message.Binding, out var bridge))
    {
      // The DUI probes bindings that may not exist (00.bindings.ts hoists `nonExistantBindings` first, on purpose).
      // WebView2 fails those synchronously; here the probe is a pending promise with no timeout, so an unanswered
      // call stalls Nuxt's plugin chain and the app never mounts. Empty payload = "no such binding" on the JS side.
      Resolve(message?.CallId, string.Empty);
      return;
    }
    switch (message.Op)
    {
      case "GetBindingsMethodNames":
        Resolve(message.CallId, JsonConvert.SerializeObject(bridge.GetBindingsMethodNames()));
        break;
      case "RunMethod":
        bridge.RunMethod(message.MethodName ?? string.Empty, message.RequestId ?? string.Empty, message.Args ?? "[]");
        break;
      case "GetCallResult":
        Resolve(message.CallId, bridge.GetCallResult(message.RequestId ?? string.Empty) ?? string.Empty);
        break;
      case "OpenUrl":
        bridge.OpenUrl(message.Url ?? string.Empty);
        break;
      case "ShowDevTools":
        ShowDevTools();
        break;
    }
  }

  private void Resolve(string? callId, string payload)
  {
    if (callId is null)
    {
      return;
    }
    // JsonConvert.ToString yields a JS-safe quoted literal, so payload survives quotes, slashes and newlines intact
    ExecuteScript(
      $"window.__speckleEto.resolve({JsonConvert.ToString(callId)}, {JsonConvert.ToString(payload)})",
      CancellationToken.None
    );
  }

  private sealed record EtoMessage(
    string? Binding,
    string? Op,
    string? CallId,
    string? MethodName,
    string? RequestId,
    string? Args,
    string? Url
  );
}
