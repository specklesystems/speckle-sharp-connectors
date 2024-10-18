using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

public class PassthroughProgress : IProgress<ProgressArgs>
{
  private readonly Action<ProgressArgs> _progressCallback;
  private readonly Dictionary<ProgressEvent, long> _totals = new();

  public PassthroughProgress(Action<ProgressArgs> progressCallback)
  {
    _progressCallback = progressCallback;
    foreach (ProgressEvent value in Enum.GetValues(typeof(ProgressEvent)))
    {
      _totals[value] = 0;
    }
  }

  public void Report(ProgressArgs value)
  {
    if (value.ProgressEvent == ProgressEvent.DownloadBytes)
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
