using System.Diagnostics;

namespace Speckle.Connectors.Common.Extensions;

public static class StopwatchPollyfills
{
#if !NET7_0_OR_GREATER
  private static readonly double s_tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
#endif

  public static TimeSpan GetElapsedTime(long startingTimestamp)
  {
#if NET7_0_OR_GREATER
    return Stopwatch.GetElapsedTime(startingTimestamp);
#else

    long elapsedTicks = Stopwatch.GetTimestamp() - startingTimestamp;
    return new TimeSpan((long)(elapsedTicks * s_tickFrequency));
#endif
  }
}
