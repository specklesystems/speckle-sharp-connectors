using Rhino;
using Speckle.Importers.Rhino.Internal.FileTypeConfig;
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
      var config = GetConfig(Path.GetExtension(filePath));

      RhinoDoc.ActiveDoc = open;
      if (!open.Import(filePath, config.ImportOptions))
      {
        throw new SpeckleException("Rhino could not import this file");
      }

      config.PreProcessDocument(open);

      var version = await sender.Send(projectId, modelId, account, cancellationToken);
      return version;
    }
    finally
    {
      //Being a bit extra defensive to ensure we're cleaning up the old doc
      RhinoDoc.ActiveDoc?.Dispose();
      RhinoDoc.ActiveDoc = null;
    }
  }

  private static IFileTypeConfig GetConfig(string extension) =>
    extension.ToLowerInvariant() switch
    {
      ".skp" => new SketchupConfig(),
      _ => new DefaultConfig(),
    };
}
