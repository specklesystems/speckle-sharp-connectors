using Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstance(Sender sender)
{
  public async Task<Version> RunRhinoImport(
    string filePath,
    string projectId,
    string modelId,
    Account account,
    CancellationToken cancellationToken
  )
  {
    using RhinoDoc open = RhinoDoc.CreateHeadless(null);
    try
    {
      RhinoDoc.ActiveDoc = open;
      if (!open.Import(filePath))
      {
        throw new SpeckleException("Rhino could not import this file");
      }

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
