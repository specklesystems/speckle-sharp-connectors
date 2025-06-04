using Speckle.InterfaceGenerator;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class SendProgressState : ISendProgressState
{
  public bool PreviouslyFromCacheOrSerialized { get; set; }
  public long Total { get; set; }
}

[GenerateAutoInterface]
public class SendProgress(IProgressDisplayManager progressDisplayManager, ISendProgressState sendProgressState)
  : ISendProgress
{
  private string? _previousSpeed;
  private double? _previousUploaded;

  public void Begin() => progressDisplayManager.Begin();

  public void Report(IProgress<CardProgress> onOperationProgressed, ProgressArgs args)
  {
    if (args.ProgressEvent == ProgressEvent.FromCacheOrSerialized)
    {
      sendProgressState.PreviouslyFromCacheOrSerialized = args.Count >= args.Total;
    }
    else if (args.ProgressEvent == ProgressEvent.FindingChildren)
    {
      sendProgressState.Total = args.Count;
    }
    else if (args.ProgressEvent == ProgressEvent.UploadBytes)
    {
      _previousSpeed = progressDisplayManager.CalculateSpeed(args);
    }
    if (!progressDisplayManager.ShouldUpdate())
    {
      return;
    }

    switch (args.ProgressEvent)
    {
      case ProgressEvent.CachedToLocal:
        if (!sendProgressState.PreviouslyFromCacheOrSerialized)
        {
          return;
        }
        onOperationProgressed.Report(
          new($"Caching... ({args.Count} objects)", progressDisplayManager.CalculatePercentage(args))
        );
        break;
      case ProgressEvent.UploadedObjects
      case ProgressEvent.UploadBytes:
        if (!sendProgressState.PreviouslyFromCacheOrSerialized)
        {
          return;
        }
        onOperationProgressed.Report(new($"Uploading... {_previousUploaded} ({_previousSpeed})", null));
        break;
      case ProgressEvent.FromCacheOrSerialized:
        var message = $"Serializing... ({args.Count} / {sendProgressState.Total} found objects)";
        onOperationProgressed.Report(new(message, progressDisplayManager.CalculatePercentage(args)));
        break;
    }
  }
}
