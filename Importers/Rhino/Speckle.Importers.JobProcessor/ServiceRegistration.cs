using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Importers.JobProcessor.Blobs;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Sdk;

namespace Speckle.Importers.JobProcessor;

internal static class ServiceRegistration
{
  private static readonly Application s_application = new(".NET File Import Job Processor", "jobprocessor");

  public static IServiceCollection AddJobProcessor(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddLoggingConfig();
    serviceCollection.AddTransient<JobProcessorInstance>();
    serviceCollection.AddTransient<Repository>();
    serviceCollection.AddTransient<ImportJobFileDownloader>();
    return serviceCollection;
  }

  private static IServiceCollection AddLoggingConfig(this IServiceCollection serviceCollection)
  {
    serviceCollection.Initialize(s_application, HostAppVersion.v3);

    return serviceCollection;
  }
}
