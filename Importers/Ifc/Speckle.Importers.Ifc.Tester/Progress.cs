using Speckle.Sdk.Transports;

namespace Speckle.Importers.Ifc.Tester;

public class Progress(bool write) : IProgress<ProgressArgs>
{
  private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(1000);
  private DateTime _lastTime = DateTime.UtcNow;

  private long _totalBytes;

  public void Report(ProgressArgs value)
  {
    if (write)
    {
      if (value.ProgressEvent == ProgressEvent.DownloadBytes)
      {
        Interlocked.Add(ref _totalBytes, value.Count);
      }
      var now = DateTime.UtcNow;
      if (now - _lastTime >= _debounce)
      {
        if (value.ProgressEvent == ProgressEvent.DownloadBytes)
        {
          Console.WriteLine(value.ProgressEvent + " t " + _totalBytes);
        }
        else
        {
          Console.WriteLine(value.ProgressEvent + " c " + value.Count + " t " + value.Total);
        }

        _lastTime = now;
      }
    }
  }
}
