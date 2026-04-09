using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

//this aggregates speed across all SDK uploads and passes it to the main thread
public class PassthroughProgress : IProgress<ProgressArgs>
{
  private readonly Action<ProgressArgs> _progressCallback;
  private readonly Dictionary<ProgressEvent, long> _totals = new();
#if NET5_0_OR_GREATER
  private readonly ProgressEvent[] _progressEventTypes = Enum.GetValues<ProgressEvent>();
#else
  private readonly Array _progressEventTypes = Enum.GetValues(typeof(ProgressEvent));
#endif
  public PassthroughProgress(Action<ProgressArgs> progressCallback)
  {
    _progressCallback = progressCallback;
    foreach (ProgressEvent value in _progressEventTypes)
    {
      _totals[value] = 0;
    }
  }

  public void Report(ProgressArgs value)
  {
    if (value.ProgressEvent == ProgressEvent.DownloadBytes || value.ProgressEvent == ProgressEvent.UploadBytes)
    {
      long totalBytes;
      lock (_totals)
      {
        _totals[value.ProgressEvent] += value.Count;
        totalBytes = _totals[value.ProgressEvent];
      }

      _progressCallback(value with { Count = totalBytes });
    }
    else
    {
      _progressCallback(value);
    }
  }
}
