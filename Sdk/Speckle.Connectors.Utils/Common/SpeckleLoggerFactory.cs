using Microsoft.Extensions.Logging;
using Speckle.Logging;

namespace Speckle.Connectors.Utils.Common;

#pragma warning disable CA1063
public class SpeckleLoggerFactory : ILoggerFactory
#pragma warning restore CA1063
{
#pragma warning disable CA1816
#pragma warning disable CA1063
  public void Dispose() { }
#pragma warning restore CA1063
#pragma warning restore CA1816

  public ILogger CreateLogger(string categoryName) => new SpeckleLogger(SpeckleLog.Create(categoryName));

  public void AddProvider(ILoggerProvider provider) => throw new NotImplementedException();
}

public class SpeckleLogger : ILogger
{
  private readonly ISpeckleLogger _logger;

  public SpeckleLogger(ISpeckleLogger logger)
  {
    _logger = logger;
  }

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
    Func<TState, Exception, string> formatter)
  {
    switch (logLevel)
    {
      case LogLevel.Critical:
        _logger.Fatal(exception, formatter(state, exception));
        break;
      case LogLevel.Trace:
        _logger.Debug(exception, formatter(state, exception));
        break;
      case LogLevel.Debug:
        _logger.Debug(exception, formatter(state, exception));
        break;
      case LogLevel.Information:
        _logger.Information(exception, formatter(state, exception));
        break;
      case LogLevel.Warning:
        _logger.Warning(exception, formatter(state, exception));
        break;
      case LogLevel.Error:
        _logger.Error(exception, formatter(state, exception));
        break;
      case LogLevel.None:
      default:
        throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
    }
  }

  public bool IsEnabled(LogLevel logLevel) => true;

  public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
}
