using Microsoft.Extensions.Logging;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.Utils;

public class SpeckleLogger(ISpeckleLogger logger) : ILogger
{
  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception?, string> formatter
  )
  {
    switch (logLevel)
    {
      case LogLevel.Critical:
        logger.Write(SpeckleLogLevel.Fatal, exception, formatter(state, exception));
        break;
      case LogLevel.Trace:
        logger.Write(SpeckleLogLevel.Verbose, exception, formatter(state, exception));
        break;
      case LogLevel.Debug:
        logger.Write(SpeckleLogLevel.Debug, exception, formatter(state, exception));
        break;
      case LogLevel.Information:
        logger.Write(SpeckleLogLevel.Information, exception, formatter(state, exception));
        break;
      case LogLevel.Warning:
        logger.Write(SpeckleLogLevel.Warning, exception, formatter(state, exception));
        break;
      case LogLevel.Error:
        logger.Write(SpeckleLogLevel.Error, exception, formatter(state, exception));
        break;
      case LogLevel.None:
      default:
        throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
    }
  }

  public bool IsEnabled(LogLevel logLevel) => true;

  public IDisposable BeginScope<TState>(TState state)
    where TState : notnull => throw new NotImplementedException();
}
