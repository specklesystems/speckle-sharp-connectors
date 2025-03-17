using Serilog.Events;

namespace Speckle.Connectors.Logging.Internal;

internal static class SpeckleLogLevelUtility
{
  internal static LogEventLevel GetLevel(SpeckleLogLevel speckleLogLevel) =>
    speckleLogLevel switch
    {
      SpeckleLogLevel.Debug => LogEventLevel.Debug,
      SpeckleLogLevel.Verbose => LogEventLevel.Verbose,
      SpeckleLogLevel.Information => LogEventLevel.Information,
      SpeckleLogLevel.Warning => LogEventLevel.Warning,
      SpeckleLogLevel.Error => LogEventLevel.Error,
      SpeckleLogLevel.Fatal => LogEventLevel.Fatal,
      _ => throw new ArgumentOutOfRangeException(nameof(speckleLogLevel), speckleLogLevel, null)
    };
}
