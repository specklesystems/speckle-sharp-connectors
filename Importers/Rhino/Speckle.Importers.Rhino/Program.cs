using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RhinoInside;
using Speckle.Importers.Rhino.Internal;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

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

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "IPC")]
  public static async Task Main(string[] args)
  {
    ILogger? logger = null;
    ImporterInstance? importer = null;
    
    try
    {
      var importerArgs = JsonSerializer.Deserialize<ImporterArgs>(args[0], s_serializerOptions);

      var serviceCollection = new ServiceCollection();
      serviceCollection.AddRhinoImporter(importerArgs.HostApplication);
      using var serviceProvider = serviceCollection.BuildServiceProvider();
      logger = serviceProvider.GetRequiredService<ILogger<object>>();
      TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        logger.LogCritical(eventArgs.Exception, "Unobserved Task Exception");
    
      
      var factory = serviceProvider.GetRequiredService<ImporterInstanceFactory>();
      
      // Error handling flow below here looks a bit of a mess, but we're having to navigate threading issues with rhino inside
      // https://discourse.mcneel.com/t/rhino-inside-fatal-app-crashes-when-disposing-headless-documents/208673/7
      try
      {
        // This needs to be called on the main thread
        importer = factory.Create(importerArgs);  
      }
      catch (Exception ex)
      {
        WriteResult(new() { ErrorMessage = ex.Message }, importerArgs.ResultsPath);
        return;
      }
      
      // As soon as the main thread is yielded, it will be hogged by Rhino
      // Task.Run ensures we run everything on a thread pool thread.
      await Task.Run(async () =>
      {
        try
        {
          Version result = await importer.RunRhinoImport(importerArgs, CancellationToken.None).ConfigureAwait(false);
          WriteResult(new() { Version = result }, importerArgs.ResultsPath);
        }
        catch(Exception ex)
        {
          WriteResult(new() { ErrorMessage = ex.Message }, importerArgs.ResultsPath);
        }
      });
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
    finally
    {
      importer?.Dispose();
    }
    
  }

  private static void WriteResult(ImporterResponse result, string resultsPath)
  {
    var serializedResult = JsonSerializer.Serialize(result, s_serializerOptions);
    File.WriteAllLines(resultsPath, [serializedResult]);
  }
}
