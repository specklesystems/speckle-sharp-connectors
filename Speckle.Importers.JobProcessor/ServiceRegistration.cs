using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;

namespace Speckle.Importers.JobProcessor;

public static class ServiceRegistration
{
  public static IServiceCollection AddJobProcessor(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddLogging();
    serviceCollection.AddLoggingConfig();
    serviceCollection.AddTransient<JobProcessorInstance>();
    serviceCollection.AddTransient<Repository>();
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
