using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RhinoInside;
using Speckle.Importers.Rhino.Internal;

namespace Speckle.Importers.Rhino;

public static class Program
{
  static Program()
  {
    Resolver.Initialize();
    Console.WriteLine($"Loading Rhino @ {Resolver.RhinoSystemDirectory}");
  }

  public static async Task Main(string[] args)
  {
    Thread.Sleep(2000);
    ILogger? logger = null;
    try
    {
      var serviceCollection = new ServiceCollection();
      serviceCollection.AddRhinoImporter();
      using var serviceProvider = serviceCollection.BuildServiceProvider();
      logger = serviceProvider.GetRequiredService<ILogger<object>>();
      TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        logger.LogCritical(eventArgs.Exception, "Unobserved Task Exception");

      var importer = serviceProvider.GetRequiredService<ImporterInstance>();

      await Task.Run(async () =>
        {
          await importer.Run(args, CancellationToken.None);
        })
        .ConfigureAwait(false);
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
