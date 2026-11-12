using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.DUI.EtoWebView;

namespace Speckle.Connectors.DUI.Tests.Bridge;

public class EtoMessageDispatcherTests
{
  private sealed class FakeBridge : IEtoBindingBridge
  {
    public List<(string Method, string RequestId, string Args, string? Context)> Runs { get; } = [];
    public List<Uri> OpenedUrls { get; } = [];
    public string? CallResult { get; set; } = "{\"ok\":true}";
    public Exception? ThrowOnCallResult { get; set; }

    public string[] GetBindingsMethodNames() => ["GetAccounts"];

    public string[] GetBridgeCapabilities() => ["otelTraceContext"];

    public void RunMethodTraced(string methodName, string requestId, string methodArgs, string? otelTraceContext) =>
      Runs.Add((methodName, requestId, methodArgs, otelTraceContext));

    public string? GetCallResult(string requestId) => ThrowOnCallResult is null ? CallResult : throw ThrowOnCallResult;

    public void OpenUrl(Uri url) => OpenedUrls.Add(url);
  }

  private FakeBridge _bridge = null!;
  private List<(string CallId, string Payload)> _resolved = null!;
  private int _devToolsShown;
  private EtoMessageDispatcher _sut = null!;

  [SetUp]
  public void SetUp()
  {
    _bridge = new FakeBridge();
    _resolved = [];
    _devToolsShown = 0;
    _sut = new EtoMessageDispatcher(
      binding => binding == "accountsBinding" ? _bridge : null,
      (callId, payload) => _resolved.Add((callId, payload)),
      () => _devToolsShown++
    );
  }

  [Test]
  public void UnknownBinding_IsAnsweredWithEmptyPayload()
  {
    _sut.Dispatch("""{"binding":"nonExistantBindings","op":"GetBindingsMethodNames","callId":"c1"}""");

    _resolved.Should().Equal(("c1", ""));
  }

  [Test]
  public void UnknownOp_IsAnsweredWithEmptyPayload()
  {
    _sut.Dispatch("""{"binding":"accountsBinding","op":"SomethingNewer","callId":"c1"}""");

    _resolved.Should().Equal(("c1", ""));
  }

  [Test]
  public void GetBindingsMethodNames_IsAnsweredWithTheNamesAsJson()
  {
    _sut.Dispatch("""{"binding":"accountsBinding","op":"GetBindingsMethodNames","callId":"c1"}""");

    _resolved.Should().Equal(("c1", """["GetAccounts"]"""));
  }

  [Test]
  public void GetBridgeCapabilities_IsAnsweredWithTheCapabilitiesAsJson()
  {
    _sut.Dispatch("""{"binding":"accountsBinding","op":"GetBridgeCapabilities","callId":"c1"}""");

    _resolved.Should().Equal(("c1", """["otelTraceContext"]"""));
  }

  [Test]
  public void RunMethodTraced_PassesTheTraceContextThrough()
  {
    _sut.Dispatch(
      """{"binding":"accountsBinding","op":"RunMethodTraced","methodName":"GetAccounts","requestId":"r1","args":"[]","otelTraceContext":"{\"traceparent\":\"00-a-b-01\"}"}"""
    );

    _bridge.Runs.Should().Equal(("GetAccounts", "r1", "[]", """{"traceparent":"00-a-b-01"}"""));
    _resolved.Should().BeEmpty();
  }

  [Test]
  public void RunMethod_FromAnOlderDui_RunsWithoutTraceContext()
  {
    _sut.Dispatch("""{"binding":"accountsBinding","op":"RunMethod","methodName":"GetAccounts","requestId":"r1"}""");

    _bridge.Runs.Should().Equal(("GetAccounts", "r1", "[]", null));
  }

  [Test]
  public void GetCallResult_WithNoResult_IsAnsweredWithEmptyPayload()
  {
    _bridge.CallResult = null;

    _sut.Dispatch("""{"binding":"accountsBinding","op":"GetCallResult","requestId":"r1","callId":"c1"}""");

    _resolved.Should().Equal(("c1", ""));
  }

  [Test]
  public void ACallThatThrows_IsStillAnswered_AndTheExceptionPropagates()
  {
    _bridge.ThrowOnCallResult = new ArgumentException("No result for the given request id was found: r1");

    var dispatch = () =>
      _sut.Dispatch("""{"binding":"accountsBinding","op":"GetCallResult","requestId":"r1","callId":"c1"}""");

    dispatch.Should().Throw<ArgumentException>();
    _resolved.Should().Equal(("c1", ""));
  }

  [Test]
  public void OpenUrl_OpensWebPages()
  {
    _sut.Dispatch("""{"binding":"accountsBinding","op":"OpenUrl","url":"https://app.speckle.systems/projects"}""");

    _bridge.OpenedUrls.Should().Equal(new Uri("https://app.speckle.systems/projects"));
  }

  [TestCase("file:///Applications/Calculator.app")]
  [TestCase("/Users/someone/Desktop/run.command")]
  [TestCase("not a url")]
  public void OpenUrl_RefusesAnythingButHttp(string url)
  {
    var dispatch = () => _sut.Dispatch($$"""{"binding":"accountsBinding","op":"OpenUrl","url":"{{url}}"}""");

    dispatch.Should().Throw<ArgumentException>();
    _bridge.OpenedUrls.Should().BeEmpty();
  }

  [Test]
  public void ShowDevTools_IsForwardedToTheHost()
  {
    _sut.Dispatch("""{"binding":"accountsBinding","op":"ShowDevTools"}""");

    _devToolsShown.Should().Be(1);
  }
}
