using Microsoft.Extensions.Logging;
using Speckle.Connectors.Logging;

namespace Speckle.Connectors.Common;

public class SpeckleLogger(Logger logger) : ILogger
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
        logger.Write(SpeckleLogLevel.Fatal, eventId.Id, state, exception, formatter);
        break;
      case LogLevel.Trace:
        logger.Write(SpeckleLogLevel.Verbose, eventId.Id, state, exception, formatter);
        break;
      case LogLevel.Debug:
        logger.Write(SpeckleLogLevel.Debug, eventId.Id, state, exception, formatter);
        break;
      case LogLevel.Information:
        logger.Write(SpeckleLogLevel.Information, eventId.Id, state, exception, formatter);
        break;
      case LogLevel.Warning:
        logger.Write(SpeckleLogLevel.Warning, eventId.Id, state, exception, formatter);
        break;
      case LogLevel.Error:
        logger.Write(SpeckleLogLevel.Error, eventId.Id, state, exception, formatter);
        break;
      case LogLevel.None:
      default:
        throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
    }
  }

  public bool IsEnabled(LogLevel logLevel) => true;

  /// <summary>
  /// When performance is not a critical requirement, <c>IEnumerable{KeyValuePair{string, object?}}</c> can be used.
  /// it is recommended to use <c>IReadOnlyList{KeyValuePair{string, object?}}</c> as the <typeparamref name="TState" /> for the best performance.
  /// </summary>
  /// <param name="state"></param>
  /// <typeparam name="TState"></typeparam>
  /// <returns></returns>
  public IDisposable? BeginScope<TState>(TState state)
    where TState : notnull => logger.BeginScope(state);
}
