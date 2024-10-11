using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Common;
using Speckle.Connectors.Logging;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models;

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
  public static readonly string TabTitle = "Speckle (Beta)";

  public static IDisposable Initialize(
    this IServiceCollection serviceCollection,
    HostApplication application,
    HostAppVersion version
  )
  {
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);

    var (logging, tracing, metrics) = Observability.Initialize(
      application.Name + " " + HostApplications.GetVersion(version),
      application.Slug,
      Assembly.GetExecutingAssembly().GetVersion(),
#if DEBUG || LOCAL
      new(
        new SpeckleLogging(Console: true, MinimumLevel: SpeckleLogLevel.Debug),
        new SpeckleTracing(Console: false),
        new SpeckleMetrics(Console: true)
      )
#else
      new(
        new SpeckleLogging(
          Console: true,
          Otel:
          [
            new(
              Endpoint: "https://seq-dev.speckle.systems/ingest/otlp/v1/logs",
              Headers: new() { { "X-Seq-ApiKey", "y5YnBp12ZE1Czh4tzZWn" } }
            )
          ],
          MinimumLevel: SpeckleLogLevel.Warning
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
    serviceCollection.AddSpeckleSdk(application, version, Assembly.GetExecutingAssembly().GetVersion());
    serviceCollection.AddSingleton<Speckle.Sdk.Logging.ISdkActivityFactory, ConnectorActivityFactory>();
    return new LoggingDisposable(tracing, metrics);
  }
}
