// See https://aka.ms/new-console-template for more information

using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rhino.Runtime.InProcess;
using RhinoInside;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.JobProcessor.JobHandlers;
using Speckle.Importers.JobProcessor.JobQueue;

namespace Speckle.Importers.JobProcessor;

public static class Program
{
  static Program()
  {
    Resolver.Initialize();
  }

  [STAThread]
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

      var processor = serviceProvider.GetRequiredService<JobProcessorInstance>();
      logger = serviceProvider.GetRequiredService<ILogger<object>>();

      TaskScheduler.UnobservedTaskException += (sender, args) =>
        logger.LogCritical(args.Exception, "Unobserved Task Exception");

      using (new RhinoCore(["/netcore-8"], WindowStyle.NoWindow))
      {
        //What ever thread RhinoCore is created on it will grab as soon as it's available, and it will hog it forever.
        //Right now, we're giving it the main STA thread (not 100% if it needs STA or if it could work on any thread)
        await Task.Run(async () =>
          {
            try
            {
              await processor.StartProcessing();
            }
            catch (Exception ex)
            {
              logger.LogCritical(ex, "Unhandled exception in Main");
              throw;
            }
          })
          .ConfigureAwait(false);
      }
    }
    catch (Exception ex)
    {
      if (logger is not null)
      {
        logger.LogCritical(ex, "Unhandled exception reached entry point");
      }
      else
      {
        Console.WriteLine($"Unhandled exception reached entry point: {ex}");
      }

      throw;
    }
  }
}
