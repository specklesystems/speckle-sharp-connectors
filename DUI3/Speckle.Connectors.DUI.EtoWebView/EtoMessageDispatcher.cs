using Speckle.Connectors.DUI.Bridge;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI.EtoWebView;

/// <summary>The part of a binding's bridge the Eto message protocol drives.</summary>
public interface IEtoBindingBridge
{
  string[] GetBindingsMethodNames();
  string[] GetBridgeCapabilities();
  void RunMethodTraced(string methodName, string requestId, string methodArgs, string? otelTraceContext);
  string? GetCallResult(string requestId);
  void OpenUrl(Uri url);
}

/// <summary>Adapts <see cref="BrowserBridge"/>, whose host-object surface WebView2 exposes directly.</summary>
public sealed class BrowserBridgeEtoAdapter(BrowserBridge bridge) : IEtoBindingBridge
{
  public string[] GetBindingsMethodNames() => bridge.GetBindingsMethodNames();

  public string[] GetBridgeCapabilities() => bridge.GetBridgeCapabilities();

  public void RunMethodTraced(string methodName, string requestId, string methodArgs, string? otelTraceContext) =>
    bridge.RunMethodTraced(methodName, requestId, methodArgs, otelTraceContext);

  public string? GetCallResult(string requestId) => bridge.GetCallResult(requestId);

  public void OpenUrl(Uri url) => bridge.OpenUrl(url.AbsoluteUri);
}

/// <summary>
/// Dispatches the DUI's Eto script messages to binding bridges. Calls that carry a <c>callId</c> are answered through
/// <paramref name="resolve"/>; an unknown binding or op, or a call whose handling throws, is answered with an empty
/// payload so the DUI fails it at once instead of on its call timeout (FEA-124).
/// </summary>
public sealed class EtoMessageDispatcher(
  Func<string, IEtoBindingBridge?> findBridge,
  Action<string, string> resolve,
  Action showDevTools
)
{
  public void Dispatch(string raw)
  {
    var message = JsonConvert.DeserializeObject<EtoMessage>(raw);
    var bridge = message?.Binding is null ? null : findBridge(message.Binding);
    if (message is null || bridge is null)
    {
      Resolve(message?.CallId, string.Empty);
      return;
    }

    try
    {
      Dispatch(message, bridge);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      Resolve(message.CallId, string.Empty);
      throw;
    }
  }

  private void Dispatch(EtoMessage message, IEtoBindingBridge bridge)
  {
    switch (message.Op)
    {
      case "GetBindingsMethodNames":
        Resolve(message.CallId, JsonConvert.SerializeObject(bridge.GetBindingsMethodNames()));
        break;
      case "GetBridgeCapabilities":
        Resolve(message.CallId, JsonConvert.SerializeObject(bridge.GetBridgeCapabilities()));
        break;
      // RunMethod is what DUI builds before speckle-connectors-dui#145 send.
      case "RunMethod":
      case "RunMethodTraced":
        bridge.RunMethodTraced(
          message.MethodName ?? string.Empty,
          message.RequestId ?? string.Empty,
          message.Args ?? "[]",
          message.OtelTraceContext
        );
        break;
      case "GetCallResult":
        Resolve(message.CallId, bridge.GetCallResult(message.RequestId ?? string.Empty) ?? string.Empty);
        break;
      case "OpenUrl":
        bridge.OpenUrl(EnsureWebUrl(message.Url));
        break;
      case "ShowDevTools":
        showDevTools();
        break;
      default:
        Resolve(message.CallId, string.Empty);
        break;
    }
  }

  // Any script in the WebView can post OpenUrl, and the bridge hands the string to the OS opener, which on macOS also
  // runs .app bundles and .command files. The DUI only ever opens web pages. FEA-124.
  private static Uri EnsureWebUrl(string? url)
  {
    if (
      Uri.TryCreate(url, UriKind.Absolute, out var uri)
      && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
    )
    {
      return uri;
    }
    throw new ArgumentException($"OpenUrl only opens http(s) URLs, got '{url}'.", nameof(url));
  }

  private void Resolve(string? callId, string payload)
  {
    if (callId is not null)
    {
      resolve(callId, payload);
    }
  }

  private sealed record EtoMessage(
    string? Binding,
    string? Op,
    string? CallId,
    string? MethodName,
    string? RequestId,
    string? Args,
    string? OtelTraceContext,
    string? Url
  );
}
