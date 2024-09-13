using Serilog.Events;

namespace Speckle.Connectors.Logging;

/// <summary>
/// Configuration object for the Speckle logging system.
/// </summary>

public record SpeckleObservability(SpeckleLogging? Logging = null, SpeckleTracing? Tracing = null);

public record SpeckleLogging(
  SpeckleLogLevel MinimumLevel = SpeckleLogLevel.Warning,
  bool Console = true,
  SpeckleFileLogging? File = null,
  SpeckleOtelLogging? Otel = null
);

public record SpeckleFileLogging(string? Path = null, bool Enabled = true);

public record SpeckleOtelLogging(string Endpoint, bool Enabled = true, Dictionary<string, string>? Headers = null);

public record SpeckleTracing(bool Console = false, SpeckleOtelTracing? Otel = null);

public record SpeckleOtelTracing(
  string? Endpoint = null,
  bool Enabled = true,
  Dictionary<string, string>? Headers = null
);

public enum SpeckleLogLevel
{
  /// <summary>
  /// Anything and everything you might want to know about
  /// a running block of code.
  /// </summary>
  Verbose,

  /// <summary>
  /// Internal system events that aren't necessarily
  /// observable from the outside.
  /// </summary>
  Debug,

  /// <summary>
  /// The lifeblood of operational intelligence - things
  /// happen.
  /// </summary>
  Information,

  /// <summary>
  /// Service is degraded or endangered.
  /// </summary>
  Warning,

  /// <summary>
  /// Functionality is unavailable, invariants are broken
  /// or data is lost.
  /// </summary>
  Error,

  /// <summary>
  /// If you have a pager, it goes off when one of these
  /// occurs.
  /// </summary>
  Fatal
}

public interface ISpeckleLogger
{
  void Write(SpeckleLogLevel speckleLogLevel, string message, params object?[] arguments);
  void Write(SpeckleLogLevel speckleLogLevel, Exception? exception, string message, params object?[] arguments);

  void Debug(string message, params object?[] arguments);
  void Debug(Exception? exception, string message, params object?[] arguments);
  void Warning(string message, params object?[] arguments);
  void Warning(Exception? exception, string message, params object?[] arguments);
  void Information(string message, params object?[] arguments);

  void Information(Exception? exception, string message, params object?[] arguments);

  void LogError(string message, params object?[] arguments);
  void LogError(Exception? exception, string message, params object?[] arguments);
  void Fatal(Exception? exception, string message, params object?[] arguments);
}

internal sealed class SpeckleLogger : ISpeckleLogger
{
  private readonly Serilog.ILogger _logger;

  public SpeckleLogger(Serilog.ILogger logger)
  {
    _logger = logger;
  }

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
