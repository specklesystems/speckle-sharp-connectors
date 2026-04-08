using Speckle.InterfaceGenerator;
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public sealed class ReceiveProgress(IProgressDisplayManager progressDisplayManager) : IReceiveProgress
{
  private double? _previousPercentage;
  private string? _downloadSpeed;
  private double? _downloadPercentage;

  public void Begin() => progressDisplayManager.Begin();

  public void Report(IProgress<CardProgress> onOperationProgressed, ProgressArgs args)
  {
    switch (args.ProgressEvent)
    {
      case ProgressEvent.CacheCheck:
        _previousPercentage = progressDisplayManager.CalculatePercentage(args);
        break;
      case ProgressEvent.DownloadBytes:
        _downloadSpeed = progressDisplayManager.CalculateSpeed(args);
        break;
      case ProgressEvent.DownloadObjects:
        _downloadPercentage = progressDisplayManager.CalculatePercentage(args);
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
      case ProgressEvent.DownloadObjects:
        onOperationProgressed.Report(new($"Downloading...  ({_downloadSpeed})", _downloadPercentage));
        break;
      case ProgressEvent.DeserializeObject:
        onOperationProgressed.Report(
          new(
            $"Deserializing ... ({args.Count} / {args.Total} objects)",
            progressDisplayManager.CalculatePercentage(args)
          )
        );
        break;
    }
  }
}
