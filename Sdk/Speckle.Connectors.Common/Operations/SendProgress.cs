using Speckle.InterfaceGenerator;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendProgress(IProgressDisplayManager progressDisplayManager) : ISendProgress
{
  private string? _previousSpeed;

  public void Begin() => progressDisplayManager.Begin();

  public void Report(IProgress<CardProgress> onOperationProgressed, ProgressArgs args)
  {
    if (args.ProgressEvent == ProgressEvent.UploadBytes)
    {
      switch (args.ProgressEvent)
      {
        case ProgressEvent.UploadBytes:
          _previousSpeed = progressDisplayManager.CalculateSpeed(args);
          break;
      }
    }
    if (!progressDisplayManager.ShouldUpdate())
    {
      return;
    }

    switch (args.ProgressEvent)
    {
      case ProgressEvent.CachedToLocal:
        onOperationProgressed.Report(new($"Caching... ({args.Count} total objects)", null));
        break;
      case ProgressEvent.UploadBytes:
        onOperationProgressed.Report(new($"Uploading... ({_previousSpeed}) {args.Count}", null));
        break;
      case ProgressEvent.FromCacheOrSerialized:
        onOperationProgressed.Report(
          new(
            $"Loading cache and Serializing... ({args.Count} total objects)",
            progressDisplayManager.CalculatePercentage(args)
          )
        );
        break;
    }
  }
}
