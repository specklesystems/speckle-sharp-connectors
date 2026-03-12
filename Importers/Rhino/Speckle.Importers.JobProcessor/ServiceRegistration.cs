using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Common;
using Speckle.Connectors.Logging;
using Speckle.Importers.JobProcessor.Blobs;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Objects.Geometry;
using Speckle.Sdk;

namespace Speckle.Importers.JobProcessor;

internal static class ServiceRegistration
{
  private static readonly Application s_application = new(".NET File Import Job Processor", "jobprocessor");
  private const HostAppVersion HOST_APP_VERSION = HostAppVersion.v3;

  public static IServiceCollection AddJobProcessor(this IServiceCollection serviceCollection)
  {
    var assemblyVersion = Assembly.GetExecutingAssembly().GetVersion();

    serviceCollection.AddSpeckleSdk(
      s_application,
      HostApplications.GetVersion(HOST_APP_VERSION),
      assemblyVersion,
      typeof(Point).Assembly
    );

    serviceCollection.AddLoggingConfig();

    serviceCollection.AddTransient<Repository>();
    serviceCollection.AddTransient<ImportJobFileDownloader>();
    serviceCollection.AddHostedService<JobProcessorInstance>();
    return serviceCollection;
  }

  private static void AddLoggingConfig(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddOpenTelemetry("Speckle.Importers.JobProcessor",
      s_application,
      HOST_APP_VERSION,
#if DEBUG || LOCAL
      new SpeckleLogging(Console: true, File: new(), MinimumLevel: SpeckleLogLevel.Debug),
      new SpeckleTracing(Console: false),
      new SpeckleMetrics(Console: false)
#else
      new SpeckleLogging(
        Console: true,
        File: new(),
        Otel:
        [
          new(
            Endpoint: new Uri("https://seq.speckle.systems/ingest/otlp/v1/logs"),
            Headers: new()
            {
              // We're using a different token than connectors for seq because we want to beable to
              // trust the client's timestamps (rather than use the server's timestamps) for better tracing
              // This setting has more opportunity for abuse, so we're keeping it secret, unlike the connectors token.
              { "X-Seq-ApiKey", Environment.GetEnvironmentVariable("SEQ_API_KEY").NotNullOrWhiteSpace() }
            }
          ),
          new(
            Endpoint: new Uri("https://collector.speckle.dev/v1/logs"),
            Headers: new()
            {
              {
                "authorization",
                Environment.GetEnvironmentVariable("SPECKLE_COLLECTOR_API_TOKEN").NotNullOrWhiteSpace()
              }
            }
          )
        ],
        MinimumLevel: SpeckleLogLevel.Information
      ),
      new SpeckleTracing(
        Console: false,
        Otel:
        [
          new(
            Endpoint: new Uri("https://seq.speckle.systems/ingest/otlp/v1/traces"),
            Headers: new()
            {
              { "X-Seq-ApiKey", Environment.GetEnvironmentVariable("SEQ_API_KEY").NotNullOrWhiteSpace() }
            }
          ),
          new(
            Endpoint: new Uri("https://collector.speckle.dev/v1/traces"),
            Headers: new()
            {
              {
                "authorization",
                Environment.GetEnvironmentVariable("SPECKLE_COLLECTOR_API_TOKEN").NotNullOrWhiteSpace()
              }
            }
          )
        ]
      ),
      null
#endif
    );
  }
}
