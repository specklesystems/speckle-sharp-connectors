using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Transports;
using Speckle.Testing;

namespace Speckle.Connectors.Tests;

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
  [TestCase(1, 1, 6, 10, "5.00 bytes / sec")]
  [TestCase(1, 0, 6, 10, "0 bytes / sec")] //infinity
  [TestCase(1 * 1024 * 1024, 1, 6 * 1024 * 1024, 10 * 1024 * 1024, "5.00 MB / sec")]
  public void TestSpeed(long previousCount, long elapsed, long count, long? total, string? percentage)
  {
    var stopwatch = Create<IStopwatchManager>();
    stopwatch.Setup(x => x.ElapsedSeconds).Returns(elapsed);
    var manager = new ProgressDisplayManager(stopwatch.Object) { LastCount = previousCount };
    var p = manager.CalculateSpeed(new ProgressArgs(ProgressEvent.DownloadBytes, count, total));
    p.Should().Be(percentage);
  }
}
