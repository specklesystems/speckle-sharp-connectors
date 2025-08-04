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
  private sealed record LoggingDisposable(IDisposable Tracing, IDisposable Metrics) : IDisposable
  {
    public void Dispose()
    {
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
    var (logging, tracing, metrics) = Observability.Initialize(
      application.Name + " " + HostApplications.GetVersion(version),
      application.Slug,
      Assembly.GetExecutingAssembly().GetVersion(),
#if DEBUG || LOCAL
      new(
        new SpeckleLogging(Console: true, File: new(), MinimumLevel: SpeckleLogLevel.Verbose),
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
            )
          ],
          MinimumLevel: SpeckleLogLevel.Information
        ),
        new SpeckleTracing(
          Console: false,
          Otel:
          [
            new(
              Endpoint: "https://seq-dev.speckle.systems/ingest/otlp/v1/traces",
              Headers: new() { { "X-Seq-ApiKey", "y5YnBp12ZE1Czh4tzZWn" } }
            )
          ]
        )
      )
#endif
    );

    serviceCollection.AddLogging(x => x.AddProvider(new SpeckleLogProvider(logging)));
    serviceCollection.AddSpeckleSdk(
      application,
      HostApplications.GetVersion(version),
      Assembly.GetExecutingAssembly().GetVersion(),
      typeof(Point).Assembly
    );
    serviceCollection.AddSingleton<Speckle.Sdk.Logging.ISdkActivityFactory, ConnectorActivityFactory>();
    return new LoggingDisposable(tracing, metrics);
  }
}
