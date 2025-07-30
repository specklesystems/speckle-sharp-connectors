// See https://aka.ms/new-console-template for more information

using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Rhino.Runtime.InProcess;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Importers.Rhino;

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
    // Dapper doesn't understand how to handle JSON deserialization, so we need to tell it what types can be deserialzied
    SqlMapper.AddTypeHandler(new JsonHandler<FileimportPayload>());

    // DI setup
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddJobProcessor();

    serviceCollection.AddRhinoImporter();

    serviceCollection.AddTransient<IJobHandler, RhinoJobHandler>();

    var serviceProvider = serviceCollection.BuildServiceProvider();

    var processor = serviceProvider.GetRequiredService<JobProcessorInstance>();

    await processor.StartProcessing();
  }
}
