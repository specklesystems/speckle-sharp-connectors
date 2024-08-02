using Autofac;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Common;
using Speckle.Connectors.Utils.Operations;
using Speckle.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Speckle.Connectors.Utils;

public sealed class SpeckleLoggerFactory : ILoggerFactory
{
  public void Dispose() { }

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
        _logger.Write(SpeckleLogLevel.Fatal, exception, formatter(state, exception));
        break;
      case LogLevel.Trace:
        _logger.Write(SpeckleLogLevel.Verbose, exception, formatter(state, exception));
        break;
      case LogLevel.Debug:
        _logger.Write(SpeckleLogLevel.Debug, exception, formatter(state, exception));
        break;
      case LogLevel.Information:
        _logger.Write(SpeckleLogLevel.Information, exception, formatter(state, exception));
        break;
      case LogLevel.Warning:
        _logger.Write(SpeckleLogLevel.Warning, exception, formatter(state, exception));
        break;
      case LogLevel.Error:
        _logger.Write(SpeckleLogLevel.Error, exception, formatter(state, exception));
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

public static class ContainerRegistration
{
  public static void AddConnectorUtils(this SpeckleContainerBuilder builder)
  {
    // send operation and dependencies
    builder.AddSingleton<CancellationManager>();
    builder.AddScoped<ReceiveOperation>();
    builder.AddSingleton<AccountService>();
    builder.ScanAssemblyOfType<SendHelper>();

    builder.AddSingleton<ILoggerFactory>(new SpeckleLoggerFactory());
    builder.ContainerBuilder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
  }
}
