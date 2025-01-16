using FluentAssertions;
using NUnit.Framework;

namespace Speckle.Connectors.Tests;

public class ActivityScopeTests
{
  [Test]
  public async Task TestAsyncLocal()
  {
    Logging.ActivityScope.SetTag("test", "me");
    await Task.Delay(10);
    Logging.ActivityScope.Tags.ContainsKey("test").Should().BeTrue();
    Logging.ActivityScope.Tags["test"].Should().Be("me");
  }
}
