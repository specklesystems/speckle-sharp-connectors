using Moq;
using NUnit.Framework;
using Speckle.Connectors.Common.Operations;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Transports;
using Speckle.Testing;

namespace Speckle.Connectors.Common.Tests.Operations;

public class SendProgressTests : MoqTest
{
  [Test]
  public void Begin()
  {
    var displayManager = Create<IProgressDisplayManager>();
    displayManager.Setup(x => x.Begin());
    var progress = new SendProgress(displayManager.Object, Create<ISendProgressState>().Object);
    progress.Begin();
  }

  [Test]
  [TestCaseSource(nameof(ReportCases))]
  public void Report_Tests(ProgressEvent e, bool shouldUpdate, bool previousFromCacheOrSerialized)
  {
    var displayManager = Create<IProgressDisplayManager>();
    var sendProgressState = Create<ISendProgressState>();
    var args = new ProgressArgs(e, 1, 10);
    switch (e)
    {
      case ProgressEvent.UploadBytes:
        displayManager.Setup(x => x.CalculateSpeed(args)).Returns("asdf");
        break;
      case ProgressEvent.FromCacheOrSerialized:
        sendProgressState.SetupSet(x => x.PreviouslyFromCacheOrSerialized = false);
        break;
      case ProgressEvent.FindingChildren:
        sendProgressState.SetupSet(x => x.Total = args.Count);
        break;
      case ProgressEvent.UploadingObjects:
        break;
    }
    displayManager.Setup(x => x.ShouldUpdate()).Returns(shouldUpdate);

    var progress = new SendProgress(displayManager.Object, sendProgressState.Object);
    var cardProgress = Create<IProgress<CardProgress>>();
    if (shouldUpdate)
    {
      switch (e)
      {
        case ProgressEvent.UploadingObjects:
        case ProgressEvent.UploadBytes:
          sendProgressState.Setup(x => x.PreviouslyFromCacheOrSerialized).Returns(previousFromCacheOrSerialized);
          if (previousFromCacheOrSerialized)
          {
            cardProgress.Setup(x => x.Report(It.IsAny<CardProgress>()));
          }
          break;
        case ProgressEvent.CachedToLocal:
          sendProgressState.Setup(x => x.PreviouslyFromCacheOrSerialized).Returns(previousFromCacheOrSerialized);
          if (previousFromCacheOrSerialized)
          {
            displayManager.Setup(x => x.CalculatePercentage(args)).Returns(0.5);
            cardProgress.Setup(x => x.Report(It.IsAny<CardProgress>()));
          }
          break;
        case ProgressEvent.FromCacheOrSerialized:
          displayManager.Setup(x => x.CalculatePercentage(args)).Returns(0.5);
          cardProgress.Setup(x => x.Report(It.IsAny<CardProgress>()));
          sendProgressState.Setup(x => x.Total).Returns(11);
          break;
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
      yield return new[] { e, true, true };
      yield return new[] { e, false, true };
      yield return new[] { e, true, false };
      yield return new[] { e, false, false };
    }
  }
}
