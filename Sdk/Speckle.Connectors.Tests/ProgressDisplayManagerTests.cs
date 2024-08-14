using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Utils.Operations;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Tests;

public class ProgressDisplayManagerTests
{
  [Test]
  [TestCase(5, 10, 0.5)]
  [TestCase(null, 10, null)]
  [TestCase(1, null, null)]
  [TestCase(null, null, null)]
  [TestCase(10, 10, 1)]
  public void TestPercentage(long? count, long? total, double? percentage)
  {
    var manager = new ProgressDisplayManager();
    var p = manager.CalculatePercentage(new ProgressArgs(ProgressEvent.DownloadBytes, count, total));
    p.Should().Be(percentage);
  }
}
