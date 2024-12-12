using Speckle.InterfaceGenerator;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendProgress(IProgressDisplayManager progressDisplayManager) : ISendProgress
{
  private string? _previousSpeed;
  private bool _serializeIsDone;
  private long _serialized;
  private long _total;

  public void Begin() => progressDisplayManager.Begin();

  public void Report(IProgress<CardProgress> onOperationProgressed, ProgressArgs args)
  {
    if (args.ProgressEvent == ProgressEvent.FromCacheOrSerialized)
    {
      _serialized = args.Count;
      _serializeIsDone = args.Count >= args.Total;
    }
    else if (args.ProgressEvent == ProgressEvent.FindingChildren)
    {
      _total = args.Count;
    }
    else if (args.ProgressEvent == ProgressEvent.UploadBytes)
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
        if (!_serializeIsDone)
        {
          return;
        }
        onOperationProgressed.Report(
          new($"Caching... ({args.Count} objects)", progressDisplayManager.CalculatePercentage(args))
        );
        break;
      case ProgressEvent.UploadBytes:
        if (!_serializeIsDone)
        {
          return;
        }
        onOperationProgressed.Report(new($"Uploading... ({_previousSpeed})", null));
        break;
      case ProgressEvent.FromCacheOrSerialized:
        var message = $"Serializing... ({_serialized} / {_total} found objects)";
        onOperationProgressed.Report(new(message, progressDisplayManager.CalculatePercentage(args)));
        break;
    }
  }
}
