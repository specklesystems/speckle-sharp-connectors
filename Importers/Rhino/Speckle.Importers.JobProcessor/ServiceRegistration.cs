using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Formatting.Compact;
using Speckle.Connectors.Common;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Sdk;

namespace Speckle.Importers.JobProcessor;

internal static class ServiceRegistration
{
  private static readonly Application s_applicaiton = new(".NET File Import Job Processor", "jobprocessor");

  public static IServiceCollection AddJobProcessor(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddLogging();
    serviceCollection.AddLoggingConfig();
    serviceCollection.AddTransient<JobProcessorInstance>();
    serviceCollection.AddTransient<Repository>();
    serviceCollection.AddLogging();
    return serviceCollection;
  }

  private static IServiceCollection AddLoggingConfig(this IServiceCollection serviceCollection)
  {
    Log.Logger = new LoggerConfiguration()
      .Enrich.FromLogContext()
      .WriteTo.Console(new RenderedCompactJsonFormatter())
      .CreateLogger();
    serviceCollection.Initialize(s_applicaiton, HostAppVersion.v3);

    return serviceCollection;
  }
}
