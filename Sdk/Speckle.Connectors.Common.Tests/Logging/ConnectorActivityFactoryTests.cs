using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.Common.Tests.Logging;

public class ConnectorActivityFactoryTests
{
  private const string VALID_TRACE_PARENT = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

  [Test]
  public void StartRemote_WithoutTraceParent_DoesNotThrow()
  {
    using var sut = new ConnectorActivityFactory();

    Action start = () => sut.StartRemote(null, null, SdkActivityKind.Server, "test");

    start.Should().NotThrow();
  }

  [Test]
  public void StartRemote_WithValidTraceParent_DoesNotThrow()
  {
    using var sut = new ConnectorActivityFactory();

    Action start = () => sut.StartRemote(VALID_TRACE_PARENT, null, SdkActivityKind.Server, "test");

    start.Should().NotThrow();
  }

  [Test]
  public void StartRemote_WithMalformedTraceParent_Throws()
  {
    using var sut = new ConnectorActivityFactory();

    Action start = () => sut.StartRemote("not-a-traceparent", null, SdkActivityKind.Server, "test");

    start.Should().Throw<ArgumentException>();
  }
}
