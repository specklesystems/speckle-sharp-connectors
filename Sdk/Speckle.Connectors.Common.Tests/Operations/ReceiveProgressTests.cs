using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Transports;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Operations;

public class ReceiveProgressTests : MoqTest
{
  [Test]
  public void Begin()
  {
    var displayManager = Create<IProgressDisplayManager>();
    displayManager.Setup(x => x.Begin());
    var progress = new ReceiveProgress(displayManager.Object);
    progress.Begin();
  }

  [Test]
  [TestCaseSource(nameof(ReportCases))]
  public void Report_Tests(ProgressEvent e, bool shouldUpdate)
  {
    var displayManager = Create<IProgressDisplayManager>();
    var args = new ProgressArgs(e, 1, 10);
    switch (e)
    {
      case ProgressEvent.CacheCheck:
        displayManager.Setup(x => x.CalculatePercentage(args)).Returns(0.5);
        break;
      case ProgressEvent.DownloadBytes:
        displayManager.Setup(x => x.CalculateSpeed(args)).Returns("asdf");
        break;
      case ProgressEvent.DownloadObjects:
        displayManager.Setup(x => x.CalculatePercentage(args)).Returns(0.5);
        break;
    }
    displayManager.Setup(x => x.ShouldUpdate()).Returns(shouldUpdate);

    var progress = new ReceiveProgress(displayManager.Object);
    var cardProgress = Create<IProgress<CardProgress>>();
    if (shouldUpdate)
    {
      switch (e)
      {
        case ProgressEvent.CacheCheck:
        case ProgressEvent.DownloadBytes:
        case ProgressEvent.DownloadObjects:
        case ProgressEvent.DeserializeObject:
          cardProgress.Setup(x => x.Report(It.IsAny<CardProgress>()));
          break;
      }
      if (e == ProgressEvent.DeserializeObject)
      {
        displayManager.Setup(x => x.CalculatePercentage(args)).Returns(0.5);
      }
    }

    progress.Report(cardProgress.Object, args);
  }

  // ReSharper disable once InconsistentNaming
#pragma warning disable IDE1006
  private static readonly object[] ReportCases = GenerateReportCases().ToArray();
#pragma warning restore IDE1006

  private static IEnumerable<object> GenerateReportCases()
  {
    foreach (var e in Enum.GetValues(typeof(ProgressEvent)))
    {
      yield return new[] { e, true };
      yield return new[] { e, false };
    }
  }
}
