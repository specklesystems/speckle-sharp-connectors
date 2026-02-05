using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace Speckle.Connectors.Logging.Internal;

internal static class SpeckleLogLevelUtility
{
  internal static LogEventLevel GetSerilogLevel(SpeckleLogLevel speckleLogLevel) =>
    speckleLogLevel switch
    {
      SpeckleLogLevel.Debug => LogEventLevel.Debug,
      SpeckleLogLevel.Verbose => LogEventLevel.Verbose,
      SpeckleLogLevel.Information => LogEventLevel.Information,
      SpeckleLogLevel.Warning => LogEventLevel.Warning,
      SpeckleLogLevel.Error => LogEventLevel.Error,
      SpeckleLogLevel.Fatal => LogEventLevel.Fatal,
      _ => throw new ArgumentOutOfRangeException(nameof(speckleLogLevel), speckleLogLevel, null),
    };

  internal static LogLevel GetMicrosoftLevel(SpeckleLogLevel speckleLogLevel) =>
    speckleLogLevel switch
    {
      SpeckleLogLevel.Debug => LogLevel.Debug,
      SpeckleLogLevel.Verbose => LogLevel.Trace,
      SpeckleLogLevel.Information => LogLevel.Information,
      SpeckleLogLevel.Warning => LogLevel.Warning,
      SpeckleLogLevel.Error => LogLevel.Error,
      SpeckleLogLevel.Fatal => LogLevel.Critical,
      _ => throw new ArgumentOutOfRangeException(nameof(speckleLogLevel), speckleLogLevel, null),
    };
}
