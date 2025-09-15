using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.JobProcessor.JobHandlers;
using Speckle.Importers.JobProcessor.JobQueue;

namespace Speckle.Importers.JobProcessor;

public static class Program
{
  public static async Task Main(string[] args)
  {
    // Dapper doesn't understand how to handle JSON deserialization, so we need to tell it what types can be deserialzied
    SqlMapper.AddTypeHandler(new JsonHandler<FileimportPayload>());

    var host = ConfigureAppHost(args);

    ConfigureTopLevelLogs(host.Services.GetRequiredService<ILogger<object>>());

    await host.RunAsync();
  }

  private static IHost ConfigureAppHost(string[] args)
  {
    // DI setup
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddJobProcessor();
    builder.Services.AddWindowsService();
    builder.Services.AddTransient<IJobHandler, RhinoJobHandler>();

    return builder.Build();
  }

  private static void ConfigureTopLevelLogs(ILogger logger)
  {
    TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
      logger.LogCritical(eventArgs.Exception, "Unobserved Task Exception");

    AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
    {
      logger.LogCritical(eventArgs.ExceptionObject as Exception, "Unhandled exception occurred in the AppDomain");
    };
  }
}
