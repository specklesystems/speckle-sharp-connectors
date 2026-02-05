using Microsoft.Extensions.Logging;

namespace Speckle.Connectors.Logging;

public sealed class Logger(ILogger logger)
{
  private static LogLevel GetLevel(SpeckleLogLevel speckleLogLevel) =>
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

  public void Write<TState>(
    SpeckleLogLevel speckleLogLevel,
    int eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter
  ) => logger.Log(GetLevel(speckleLogLevel), new EventId(eventId), state, exception, formatter);
}
