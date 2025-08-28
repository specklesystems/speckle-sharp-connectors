using FluentAssertions;
using Moq;
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

  [Test]
  public void TestBegin_StartsStopwatch()
  {
    var stopwatch = Create<IStopwatchManager>();
    stopwatch.Setup(x => x.Start());
    var manager = new ProgressDisplayManager(stopwatch.Object);
    manager.Begin();
    stopwatch.Verify(x => x.Start(), Times.Once());
  }

  [Test]
  public void TestShouldUpdate_RespectsInterval()
  {
    var stopwatch = Create<IStopwatchManager>();
    var manager = new ProgressDisplayManager(stopwatch.Object);
    // Within interval, should not update
    stopwatch.Setup(x => x.ElapsedMilliseconds).Returns(0);
    manager.ShouldUpdate().Should().BeFalse();
    // Within interval, should not update
    stopwatch.Setup(x => x.ElapsedMilliseconds).Returns(100);
    manager.ShouldUpdate().Should().BeFalse();
    // After interval, should update
    stopwatch.Setup(x => x.ElapsedMilliseconds).Returns(300);
    manager.ShouldUpdate().Should().BeTrue();
  }

  [Test]
  [TestCase(-5, 10, -0.5)]
  [TestCase(0, 10, 0)]
  [TestCase(10, 5, 2)]
  [TestCase(long.MaxValue, long.MaxValue, 1)]
  public void TestPercentage_EdgeCases(long count, long? total, double? expected)
  {
    var stopwatch = Create<IStopwatchManager>();
    var manager = new ProgressDisplayManager(stopwatch.Object);
    var p = manager.CalculatePercentage(new ProgressArgs(ProgressEvent.DownloadBytes, count, total));
    p.Should().Be(expected);
  }

  [Test]
  [TestCase(1, -5, 10, "0 bytes / sec")]
  [TestCase(1, 0, 10, "0 bytes / sec")]
  [TestCase(1, long.MaxValue, 10, "8.00 EB / sec")]
  [TestCase(0, 5, 10, "0 bytes / sec")] // count = 0
  public void TestSpeed_EdgeCases(long elapsed, long count, long? total, string expected)
  {
    var stopwatch = Create<IStopwatchManager>();
    stopwatch.Setup(x => x.ElapsedSeconds).Returns(elapsed);
    var manager = new ProgressDisplayManager(stopwatch.Object);
    var p = manager.CalculateSpeed(new ProgressArgs(ProgressEvent.DownloadBytes, count, total));
    p.Should().Be(expected);
  }

  [Test]
  [TestCase(1, 100, 200, ProgressEvent.DeserializeObject, "100 objects / sec")]
  [TestCase(1, 5, 10, ProgressEvent.SerializeObject, "5.00 objects / sec")]
  [TestCase(1, 5, 10, (ProgressEvent)999, "")] // Unknown event
  public void TestSpeed_AllProgressEvents(long elapsed, long count, long? total, ProgressEvent evt, string expected)
  {
    var stopwatch = Create<IStopwatchManager>();
    stopwatch.Setup(x => x.ElapsedSeconds).Returns(elapsed);
    var manager = new ProgressDisplayManager(stopwatch.Object);
    var p = manager.CalculateSpeed(new ProgressArgs(evt, count, total));
    p.Should().Be(expected);
  }
}
