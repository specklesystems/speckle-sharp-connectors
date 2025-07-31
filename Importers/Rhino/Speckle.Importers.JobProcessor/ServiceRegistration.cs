using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Common;
using Speckle.Importers.JobProcessor.JobQueue;
using Speckle.Sdk;

namespace Speckle.Importers.JobProcessor;

internal static class ServiceRegistration
{
  public static IServiceCollection AddJobProcessor(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddLogging();
    serviceCollection.AddLoggingConfig();
    serviceCollection.AddTransient<JobProcessorInstance>();
    serviceCollection.AddTransient<Repository>();
    serviceCollection.AddSpeckleSdk(
      new Application(".NET File Import Job Processor", "jobprocessor"),
      nameof(HostAppVersion.v3),
      Assembly.GetExecutingAssembly().GetVersion()
    );
    return serviceCollection;
  }

  private static IServiceCollection AddLoggingConfig(this IServiceCollection serviceCollection)
  {
    Log.Logger = new LoggerConfiguration()
      .Enrich.FromLogContext()
      .WriteTo.Console(new RenderedCompactJsonFormatter())
      .CreateLogger();
    serviceCollection.AddLogging(loggingBuilder =>
    {
      loggingBuilder.ClearProviders();
      loggingBuilder.AddSerilog(dispose: true);
    });
    return serviceCollection;
  }
}
