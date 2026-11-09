using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Utils;
using Speckle.Testing;

namespace Speckle.Connectors.DUI.Tests.Bridge;

public class BrowserBridgeTests : MoqTest
{
  private const string VALID_TRACE_CONTEXT =
    """{"traceparent":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"}""";

  [Test]
  public void GetBridgeCapabilities_AdvertisesOtelTraceContext()
  {
    var sut = new BrowserBridge(
      Create<IThreadContext>().Object,
      Create<IJsonSerializer>().Object,
      NullLogger<BrowserBridge>.Instance,
      Create<IBrowserScriptExecutor>().Object,
      new TopLevelExceptionHandler(NullLogger<TopLevelExceptionHandler>.Instance),
      new ConnectorActivityFactory()
    );

    sut.GetBridgeCapabilities().Should().Contain("otelTraceContext");
  }

  [Test]
  public void RunMethod_WithoutTraceContext_StillExecutesTheBinding()
  {
    var (sut, binding) = CreateBridge();

    sut.RunMethod(nameof(TestBinding.Echo), "req", """["\"hi\""]""");

    binding.Calls.Should().Be(1);
  }

  // A frontend that cannot produce a trace context, or produces a broken one, must still get
  // its binding call run - the span is the expendable part, not the user's action.
  [TestCase(null)]
  [TestCase("")]
  [TestCase("not json")]
  [TestCase("{}")]
  [TestCase("""{"traceparent":"garbage"}""")]
  [TestCase(VALID_TRACE_CONTEXT)]
  public void RunMethodTraced_WithAnyTraceContext_StillExecutesTheBinding(string? otelTraceContext)
  {
    var (sut, binding) = CreateBridge();

    sut.RunMethodTraced(nameof(TestBinding.Echo), "req", """["\"hi\""]""", otelTraceContext);

    binding.Calls.Should().Be(1);
  }

  private (BrowserBridge Bridge, TestBinding Binding) CreateBridge()
  {
    var threadContext = Create<IThreadContext>();
    threadContext
      .Setup(x => x.RunOnThreadAsync(It.IsAny<Func<Task>>(), false))
      .Returns((Func<Task> action, bool _) => action());

    var scriptExecutor = Create<IBrowserScriptExecutor>();
    scriptExecutor.Setup(x => x.ExecuteScript(It.IsAny<string>(), It.IsAny<CancellationToken>()));

    var serializer = new JsonSerializer(
      new JsonSerializerSettingsFactory(new ServiceCollection().BuildServiceProvider())
    );

    var bridge = new BrowserBridge(
      threadContext.Object,
      serializer,
      NullLogger<BrowserBridge>.Instance,
      scriptExecutor.Object,
      new TopLevelExceptionHandler(NullLogger<TopLevelExceptionHandler>.Instance),
      new ConnectorActivityFactory()
    );

    var binding = new TestBinding { Parent = bridge };
    bridge.AssociateWithBinding(binding);

    return (bridge, binding);
  }

  private sealed class TestBinding : IBinding
  {
    public string Name => "testBinding";

    public IBrowserBridge Parent { get; init; } = null!;

    public int Calls { get; private set; }

    public string Echo(string value)
    {
      Calls++;
      return value;
    }
  }
}
