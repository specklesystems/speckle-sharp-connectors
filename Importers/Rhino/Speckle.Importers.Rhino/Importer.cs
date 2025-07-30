using Rhino;
using Rhino.Runtime.InProcess;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino;

public class Importer(Sender sender)
{
  public async Task<Version> Import(
    string filePath,
    string projectId,
    string modelId,
    Account account,
    CancellationToken cancellationToken
  )
  {
    using (new RhinoCore(["/netcore-8"], WindowStyle.NoWindow))
    {
      //doc is often null so dispose the active doc too
      using var doc = RhinoDoc.Open(filePath, out _);
      using var __ = RhinoDoc.ActiveDoc;

      var version = await sender.Send(projectId, modelId, account, cancellationToken);

      return version;
    }
  }
}
