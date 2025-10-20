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
  public static async Task<int> Main(string[] args)
  {
    // Dapper doesn't understand how to handle JSON deserialization, so we need to tell it what types can be deserialzied
    SqlMapper.AddTypeHandler(new JsonHandler<FileimportPayload>());

    var host = ConfigureAppHost(args);

    var backgroundServiceTasks = host
      .Services.GetServices<IHostedService>()
      .OfType<BackgroundService>()
      .Select(s => s.ExecuteTask);

    await host.RunAsync();

    if (backgroundServiceTasks.Any(t => t?.IsFaulted == true))
    {
      //https://github.com/dotnet/runtime/issues/67146
      return -1;
    }

    return 0;
  }

  private static IHost ConfigureAppHost(string[] args)
  {
    // DI setup
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddJobProcessor();
    builder.Services.AddWindowsService();
    builder.Services.AddTransient<IJobHandler, RhinoJobHandler>();

    builder.Logging.AddEventLog(settings =>
    {
      settings.SourceName = "Speckle Rhino File Import Job Processor";
      settings.Filter = (_, level) => level >= LogLevel.Information;
    });

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
