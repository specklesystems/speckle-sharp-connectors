using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Importers.Rhino.Internal;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino;

public static class Importer
{
  public static async Task<Version> Import(
    string filePath,
    string projectId,
    string modelId,
    Account account,
    CancellationToken cancellationToken
  )
  {
    // DI Setup
    // It's very important that the state of services doesn't bleed between job
    // I don't trust the current services to not hold on to caches or state that could influence the next run
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddRhinoImporter();
    var serviceProvider = serviceCollection.BuildServiceProvider();

    //doc is often null so dispose the active doc too

    using RhinoDoc open = RhinoDoc.CreateHeadless(null);
    try
    {
      RhinoDoc.ActiveDoc = open;
      if (!open.Import(filePath))
      {
        throw new SpeckleException("Rhino could not import this file");
      }

      // using RhinoDoc? doc = RhinoDoc.ActiveDoc;

      var sender = serviceProvider.GetRequiredService<Sender>();
      var version = await sender.Send(projectId, modelId, account, cancellationToken);
      return version;
    }
    finally
    {
      //Being a bit extra defensive that we're cleaning up the old doc
      RhinoDoc.ActiveDoc?.Dispose();
      RhinoDoc.ActiveDoc = null;
      GC.Collect();
    }
  }
}
