using Eto.Forms;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Newtonsoft.Json;
using Rhino;
using Speckle.Connectors.Common.Settings;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

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
  private bool _bindingsAttached;

  public DUI3EtoWebView(IServiceProvider serviceProvider, IGlobalConfigResolver globalConfigResolver)
  {
    _serviceProvider = serviceProvider;
    _browser.MessageReceived += (_, e) =>
      _serviceProvider.GetRequiredService<ITopLevelExceptionHandler>().CatchUnhandled(() => OnMessage(e.Message));
    Content = _browser;
    // bind before navigating: the page asks for method names as soon as it loads
    AttachBindings();
    _browser.Url = globalConfigResolver.GetDuiUrl();
  }

  public bool IsBrowserInitialized => _bindingsAttached;

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

  private void AttachBindings()
  {
    foreach (var binding in _serviceProvider.GetRequiredService<IEnumerable<IBinding>>())
    {
      binding.Parent.AssociateWithBinding(binding);
      // GetCallResult/OpenUrl live on the concrete bridge (they are the host-object surface, not IBrowserBridge)
      _bridges[binding.Name] = (BrowserBridge)binding.Parent;
    }
    _bindingsAttached = true;
  }

  private void OnMessage(string raw)
  {
    var message = JsonConvert.DeserializeObject<EtoMessage>(raw);
    if (message?.Binding is null || !_bridges.TryGetValue(message.Binding, out var bridge))
    {
      return; // unknown binding: the DUI logs "Failed to bind" itself
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
