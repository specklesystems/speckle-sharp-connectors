using Speckle.InterfaceGenerator;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class ProgressDisplayManager(IStopwatchManager stopwatch) : IProgressDisplayManager
{
  private long _lastMs;
  private const int UPDATE_INTERVAL = 200;

  public void Begin() => stopwatch.Start();

  public bool ShouldUpdate()
  {
    if (stopwatch.ElapsedMilliseconds < _lastMs + UPDATE_INTERVAL)
    {
      return false;
    }
    _lastMs = stopwatch.ElapsedMilliseconds;
    return true;
  }

  public double? CalculatePercentage(ProgressArgs args)
  {
    double? percentage = null;
    if (args.Total is not null)
    {
      percentage = (double)args.Count / args.Total.Value;
    }

    return percentage;
  }

  public string CalculateSpeed(ProgressArgs args)
  {
    if (args.Count == 0)
    {
      return string.Empty;
    }
    var countPerSecond = args.Count / stopwatch.ElapsedSeconds;

    switch (args.ProgressEvent)
    {
      case ProgressEvent.DownloadBytes:
      case ProgressEvent.UploadBytes:
        return $"{ToFileSize(countPerSecond)} / sec";
      case ProgressEvent.DeserializeObject:
      case ProgressEvent.SerializeObject:
        return $"{ThreeNonZeroDigits(countPerSecond)} objects / sec";
      default:
        return string.Empty;
    }
  }

  private static readonly string[] s_suffixes = ["bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB"];

  private static string ToFileSize(double value)
  {
    if (value < 0 || double.IsPositiveInfinity(value))
    {
      return ThreeNonZeroDigits(value) + " " + s_suffixes[0];
    }
    for (int i = 0; i < s_suffixes.Length; i++)
    {
      if (value <= (Math.Pow(1024, i + 1)))
      {
        return ThreeNonZeroDigits(value / Math.Pow(1024, i)) + " " + s_suffixes[i];
      }
    }

    return ThreeNonZeroDigits(value / Math.Pow(1024, s_suffixes.Length - 1)) + " " + s_suffixes[^1];
  }

  private static string ThreeNonZeroDigits(double value)
  {
    if (value < 0 || double.IsPositiveInfinity(value))
    {
      return "0";
    }
    if (value >= 100)
    {
      // No digits after the decimal.
      return value.ToString("0,0");
    }

    if (value >= 10)
    {
      // One digit after the decimal.
      return value.ToString("0.0");
    }

    // Two digits after the decimal.
    return value.ToString("0.00");
  }
}
