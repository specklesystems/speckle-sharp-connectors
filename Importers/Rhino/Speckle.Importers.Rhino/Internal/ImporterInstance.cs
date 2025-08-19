using Rhino;
using Speckle.Importers.Rhino.Internal.FileTypeConfig;
using Speckle.Sdk;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstance(Sender sender)
{
  public async Task<Version> RunRhinoImport(ImporterArgs args, CancellationToken cancellationToken)
  {
    using RhinoDoc open = RhinoDoc.CreateHeadless(null);
    try
    {
      var config = GetConfig(Path.GetExtension(args.FilePath));

      RhinoDoc.ActiveDoc = open;
      if (!open.Import(args.FilePath, config.ImportOptions))
      {
        throw new SpeckleException("Rhino could not import this file");
      }

      config.PreProcessDocument(open);

      var version = await sender.Send(args.ProjectId, args.ModelId, args.Account, cancellationToken);
      return version;
    }
    finally
    {
      //Being a bit extra defensive to ensure we're cleaning up the old doc
      // RhinoDoc.ActiveDoc?.Dispose();
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
