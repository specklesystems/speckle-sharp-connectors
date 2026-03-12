using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Logging;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;
using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.SQLite;
#if !DEBUG && !LOCAL 
using Speckle.Sdk.Common;
#endif

namespace Speckle.Importers.Rhino.Internal;

internal static class ServiceRegistration
{  
  private const HostAppVersion HOST_APP_VERSION = HostAppVersion.v3;
  
  public static IServiceCollection AddRhinoImporter(this IServiceCollection serviceCollection, Application applicationInfo)
  {
    var assemblyVersion = Assembly.GetExecutingAssembly().GetVersion();

    serviceCollection.AddSpeckleSdk(
      applicationInfo,
      HostApplications.GetVersion(HOST_APP_VERSION),
      assemblyVersion,
      typeof(Point).Assembly
    );

    serviceCollection.AddLoggingConfig(applicationInfo);
    serviceCollection.AddSingleton(applicationInfo);

    serviceCollection.AddRhino(false);
    serviceCollection.AddRhinoConverters();
    serviceCollection.AddTransient<Sender>();
    serviceCollection.AddTransient<ImporterInstance>();
    serviceCollection.AddTransient<ImporterInstanceFactory>();

    // override default thread context
    serviceCollection.AddSingleton<IThreadContext>(new ImporterThreadContext());

    // override sqlite cache, since we don't want to persist to disk any object data
    serviceCollection.AddTransient<ISqLiteJsonCacheManagerFactory, DummySqliteJsonCacheManagerFactory>();

    return serviceCollection;
  }
  
  private static void AddLoggingConfig(this IServiceCollection serviceCollection, Application applicationInfo )
  {
    serviceCollection.AddOpenTelemetry("Speckle.Importers.Rhino",
      applicationInfo,
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
