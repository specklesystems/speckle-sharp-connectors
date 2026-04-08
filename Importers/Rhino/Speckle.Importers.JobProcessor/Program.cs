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

    using var loggerDisposable = ConfigureAppHost(args, out IHost host);

    await host.RunAsync();
  }

  private static IDisposable ConfigureAppHost(string[] args, out IHost host)
  {
    // DI setup
    var builder = Host.CreateApplicationBuilder(args);

    var loggingDisposable = builder.Services.AddJobProcessor();
    builder.Services.AddWindowsService();
    builder.Services.AddTransient<IJobHandler, RhinoJobHandler>();

    builder.Logging.AddEventLog(settings =>
    {
      settings.SourceName = "Speckle Rhino File Import Job Processor";
      settings.Filter = (_, level) => level >= LogLevel.Information;
    });

    host = builder.Build();
    return loggingDisposable;
  }
}
