using Serilog.Events;

namespace Speckle.Connectors.Logging;

public sealed class Logger
{
  private readonly Serilog.ILogger _logger;

  public Logger(Serilog.ILogger logger)
  {
    _logger = logger;
  }

  private static LogEventLevel GetLevel(SpeckleLogLevel speckleLogLevel) =>
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

  public void Write(SpeckleLogLevel speckleLogLevel, string message, params object?[] arguments) =>
    _logger.Write(GetLevel(speckleLogLevel), message, arguments);

  public void Write(
    SpeckleLogLevel speckleLogLevel,
    Exception? exception,
    string message,
    params object?[] arguments
  ) => _logger.Write(GetLevel(speckleLogLevel), exception, message, arguments);

  public void Debug(string message, params object?[] arguments) => _logger.Debug(message, arguments);

  public void Debug(Exception? exception, string message, params object?[] arguments) =>
    _logger.Debug(exception, message, arguments);

  public void Warning(string message, params object?[] arguments) => _logger.Warning(message, arguments);

  public void Warning(Exception? exception, string message, params object?[] arguments) =>
    _logger.Warning(exception, message, arguments);

  public void Information(string message, params object?[] arguments) => _logger.Information(message, arguments);

  public void Information(Exception? exception, string message, params object?[] arguments) =>
    _logger.Information(exception, message, arguments);

  public void LogError(string message, params object?[] arguments) => _logger.Error(message, arguments);

  public void LogError(Exception? exception, string message, params object?[] arguments) =>
    _logger.Error(exception, message, arguments);

  public void Fatal(Exception? exception, string message, params object?[] arguments) =>
    _logger.Fatal(exception, message, arguments);
}
