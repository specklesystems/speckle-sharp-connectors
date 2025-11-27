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

    await host.RunAsync();

    return Environment.ExitCode;
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
}
