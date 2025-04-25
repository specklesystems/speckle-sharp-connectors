using FluentAssertions;
using NUnit.Framework;

namespace Speckle.Connectors.Common.Tests.Logging;

public class ActivityScopeTests
{
  [Test]
  public async Task TestAsyncLocal()
  {
    Connectors.Logging.ActivityScope.SetTag("test", "me");
    await Task.Delay(10);
    Connectors.Logging.ActivityScope.Tags.ContainsKey("test").Should().BeTrue();
    Connectors.Logging.ActivityScope.Tags["test"].Should().Be("me");
  }
}
