using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Common;
using Speckle.Connectors.Logging;
using Speckle.Objects.Geometry;
using Speckle.Sdk;

namespace Speckle.Connectors.Common;

public static class Connector
{
  private sealed record LoggingDisposable(IDisposable Logging, IDisposable Tracing, IDisposable Metrics) : IDisposable
  {
    public void Dispose()
    {
      Logging.Dispose();
      Tracing.Dispose();
      Metrics.Dispose();
    }
  }

  private const string CONNECTOR_GATEWAY_TOKEN = "7a6754bf-a883-49a7-afc4-f74ddd25e9c1";
  public static readonly string TabName = "Speckle";
  public static readonly string TabTitle = "Speckle";

  public static IDisposable Initialize(
    this IServiceCollection serviceCollection,
    Application application,
    HostAppVersion version
  )
  {
    var assemblyVersion = Assembly.GetExecutingAssembly().GetVersion();
    serviceCollection.AddSpeckleSdk(
      application,
      HostApplications.GetVersion(version),
      assemblyVersion,
      typeof(Point).Assembly
    );

    return serviceCollection.AddOpenTelemetry(
      "Connector",
      application,
      version,
      new SpeckleLogging(
        Console: true,
        File: new(),
        Otel:
        [
          new(
            Endpoint: new Uri("https://connectors.collector.speckle.dev/v1/logs"),
            Headers: new() { { "authorization", CONNECTOR_GATEWAY_TOKEN } }
          ),
        ],
        MinimumLevel: SpeckleLogLevel.Information
      ),
      new SpeckleTracing(
        Console: false,
        Otel:
        [
          new(
            Endpoint: new Uri("https://connectors.collector.speckle.dev/v1/traces"),
            Headers: new() { { "authorization", CONNECTOR_GATEWAY_TOKEN } }
          ),
        ]
      ),
      null
    );
  }

  public static IDisposable AddOpenTelemetry(
    this IServiceCollection serviceCollection,
    string serviceName,
    Application application,
    HostAppVersion version,
    SpeckleLogging loggingConfig,
    SpeckleTracing? tracingConfig,
    SpeckleMetrics? metricsConfig
  )
  {
    var assemblyVersion = Assembly.GetExecutingAssembly().GetVersion();
    var (logging, tracing, metrics) = Observability.Initialize(
      serviceName,
      application.Name + " " + HostApplications.GetVersion(version),
      application.Slug,
      assemblyVersion,
      new(loggingConfig, tracingConfig, metricsConfig)
    );
    //do this after the AddSpeckleSdk so that the logging system gets values from here.
    serviceCollection.AddLogging(x =>
    {
      x.ClearProviders();
      x.AddProvider(new SpeckleLogProvider(logging));
      x.SetMinimumLevel(GetMicrosoftLevel(loggingConfig.MinimumLevel));
    });
    serviceCollection.AddSingleton<Speckle.Sdk.Logging.ISdkActivityFactory, ConnectorActivityFactory>();
    return new LoggingDisposable(logging, tracing, metrics);
  }

  private static LogLevel GetMicrosoftLevel(SpeckleLogLevel speckleLogLevel) =>
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
