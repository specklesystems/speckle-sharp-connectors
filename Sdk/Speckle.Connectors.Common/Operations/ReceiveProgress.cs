using Speckle.InterfaceGenerator;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public sealed class ReceiveProgress(IProgressDisplayManager progressDisplayManager) : IReceiveProgress
{
  private double? _previousPercentage;
  private string? _previousSpeed;

  public void Begin() => progressDisplayManager.Begin();

  public void Report(IProgress<CardProgress> onOperationProgressed, ProgressArgs args)
  {
    {
      switch (args.ProgressEvent)
      {
        case ProgressEvent.CacheCheck:
          _previousPercentage = progressDisplayManager.CalculatePercentage(args);
          break;
        case ProgressEvent.DownloadBytes:
          _previousSpeed = progressDisplayManager.CalculateSpeed(args);
          break;
      }

      if (!progressDisplayManager.ShouldUpdate())
      {
        return;
      }

      switch (args.ProgressEvent)
      {
        case ProgressEvent.CacheCheck:
          onOperationProgressed.Report(new("Checking cache... ", _previousPercentage));
          break;
        case ProgressEvent.DownloadBytes:
          onOperationProgressed.Report(new($"Downloading... ({_previousSpeed})", null));
          break;
        case ProgressEvent.DeserializeObject:
          onOperationProgressed.Report(new("Deserializing ...", progressDisplayManager.CalculatePercentage(args)));
          break;
      }
    }
  }
}
