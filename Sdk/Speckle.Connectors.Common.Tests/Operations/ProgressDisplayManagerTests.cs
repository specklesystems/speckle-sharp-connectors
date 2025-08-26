using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Transports;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Operations;

public class ProgressDisplayManagerTests : MoqTest
{
  [Test]
  [TestCase(5, 10, 0.5)]
  [TestCase(1, null, null)]
  [TestCase(10, 10, 1)]
  public void TestPercentage(long count, long? total, double? percentage)
  {
    var stopwatch = Create<IStopwatchManager>();
    var manager = new ProgressDisplayManager(stopwatch.Object);
    var p = manager.CalculatePercentage(new ProgressArgs(ProgressEvent.DownloadBytes, count, total));
    p.Should().Be(percentage);
  }

  [Test]
  [SetCulture("en-GB")]
  [TestCase(1, 5, 10, "5.00 bytes / sec")]
  [TestCase(0, 5, 10, "0 bytes / sec")] //infinity
  [TestCase(1, 5 * 1024 * 1024, 10 * 1024 * 1024, "5.00 MB / sec")]
  public void TestSpeed(long elapsed, long count, long? total, string? percentage)
  {
    var stopwatch = Create<IStopwatchManager>();
    stopwatch.Setup(x => x.ElapsedSeconds).Returns(elapsed);
    var manager = new ProgressDisplayManager(stopwatch.Object);
    var p = manager.CalculateSpeed(new ProgressArgs(ProgressEvent.DownloadBytes, count, total));
    p.Should().Be(percentage);
  }
}
