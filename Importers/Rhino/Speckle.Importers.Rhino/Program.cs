using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RhinoInside;
using Speckle.Importers.Rhino.Internal;

namespace Speckle.Importers.Rhino;

public static class Program
{
  private static readonly JsonSerializerOptions s_serializerOptions =
    new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, };

  static Program()
  {
    Resolver.Initialize();
    Console.WriteLine($"Loading Rhino @ {Resolver.RhinoSystemDirectory}");
  }

  public static async Task Main(string[] args)
  {
    ILogger? logger = null;
    try
    {
      var importerArgs = JsonSerializer.Deserialize<ImporterArgs>(args[0], s_serializerOptions);

      var serviceCollection = new ServiceCollection();
      serviceCollection.AddRhinoImporter(importerArgs.HostApplication);
      using var serviceProvider = serviceCollection.BuildServiceProvider();
      logger = serviceProvider.GetRequiredService<ILogger<object>>();
      TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        logger.LogCritical(eventArgs.Exception, "Unobserved Task Exception");

      var importer = serviceProvider.GetRequiredService<ImporterInstance>();

      await Task.Run(async () =>
        {
          var result = await importer.Run(importerArgs, CancellationToken.None);
          var serializedResult = JsonSerializer.Serialize(result, s_serializerOptions);
          File.WriteAllLines(importerArgs.ResultsPath, [serializedResult]);
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
