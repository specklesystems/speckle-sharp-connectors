using System.Diagnostics.CodeAnalysis;
using Autofac;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Core.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Speckle.Connectors.Utils;

public static class ContainerRegistration
{
  public static void AddConnectorUtils(this SpeckleContainerBuilder builder)
  {
    // send operation and dependencies
    builder.AddSingleton<CancellationManager>();
    builder.AddScoped<ReceiveOperation>();
    builder.AddSingleton<AccountService>();
    builder.ScanAssemblyOfType<SendHelper>();

    ILoggerFactory loggerFactory = new LoggerFactory(new []{new SpeckleLoggerProvider()});
    builder.ContainerBuilder.Register(_ => loggerFactory).As<ILoggerFactory>().SingleInstance().AutoActivate();

    builder.ContainerBuilder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
    builder.AddSingleton(loggerFactory);
  }
}

[SuppressMessage("Design", "CA1063:Implement IDisposable Correctly")]
[SuppressMessage("Usage", "CA1816:Dispose methods should call SuppressFinalize")]
public class SpeckleLoggerProvider : ILoggerProvider
{
  private sealed class SpeckleLogger(ILogger logger) : ILogger
  {

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
      Func<TState, Exception?, string> formatter)
    {
     logger.Log(logLevel, exception, formatter(state, exception));
    }

    public bool IsEnabled(LogLevel logLevel) => throw new NotImplementedException();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => logger.BeginScope(state);
  }
  public void Dispose()
  {
  }

  public ILogger CreateLogger(string categoryName) => new SpeckleLogger(SpeckleLog.GetProvider().CreateLogger(categoryName));
}
