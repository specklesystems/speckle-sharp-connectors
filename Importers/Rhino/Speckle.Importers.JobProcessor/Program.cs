using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.JobProcessor.JobHandlers;
using Speckle.Importers.JobProcessor.JobQueue;

namespace Speckle.Importers.JobProcessor;

public static class Program
{
  public static async Task Main()
  {
    ILogger? logger = null;
    try
    {
      // Dapper doesn't understand how to handle JSON deserialization, so we need to tell it what types can be deserialzied
      SqlMapper.AddTypeHandler(new JsonHandler<FileimportPayload>());

      // DI setup
      var serviceCollection = new ServiceCollection();
      serviceCollection.AddJobProcessor();

      serviceCollection.AddTransient<IJobHandler, RhinoJobHandler>();

      var serviceProvider = serviceCollection.BuildServiceProvider();
      logger = serviceProvider.GetRequiredService<ILogger<object>>();
      TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        logger.LogCritical(eventArgs.Exception, "Unobserved Task Exception");

      var processor = serviceProvider.GetRequiredService<JobProcessorInstance>();

      await processor.StartProcessing();
    }
    catch (Exception ex)
    {
      const string MESSAGE = "Unhandled exception reached entry point";
      if (logger is not null)
      {
        logger.LogCritical(ex, MESSAGE);
      }
      else
      {
        Console.WriteLine(MESSAGE);
      }
      throw;
    }
  }
}
