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

#if DEBUG || LOCAL
    var minimumLevel = SpeckleLogLevel.Debug;
#else
    var minimumLevel = SpeckleLogLevel.Information;
#endif
    var (logging, tracing, metrics) = Observability.Initialize(
      application.Name + " " + HostApplications.GetVersion(version),
      application.Slug,
      assemblyVersion,
#if DEBUG || LOCAL
      new(
        new SpeckleLogging(Console: true, File: new(), MinimumLevel: minimumLevel),
        new SpeckleTracing(Console: false),
        new SpeckleMetrics(Console: false)
      )
#else
      new(
        new SpeckleLogging(
          Console: true,
          File: new(),
          Otel:
          [
            new(
              Endpoint: "https://seq-dev.speckle.systems/ingest/otlp/v1/logs",
              Headers: new() { { "X-Seq-ApiKey", "y5YnBp12ZE1Czh4tzZWn" } }
            ),
          ],
          MinimumLevel: minimumLevel
        ),
        new SpeckleTracing(
          Console: false,
          Otel:
          [
            new(
              Endpoint: "https://seq-dev.speckle.systems/ingest/otlp/v1/traces",
              Headers: new() { { "X-Seq-ApiKey", "y5YnBp12ZE1Czh4tzZWn" } }
            ),
          ]
        )
      )
#endif
    );
    //do this after the AddSpeckleSdk so that the logging system gets values from here.
    serviceCollection.AddLogging(x =>
    {
      x.ClearProviders();
      x.AddProvider(new SpeckleLogProvider(logging));
      x.SetMinimumLevel(GetMicrosoftLevel(minimumLevel));
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
