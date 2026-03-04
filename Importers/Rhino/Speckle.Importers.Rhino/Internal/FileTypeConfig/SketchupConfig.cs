using Rhino;
using Rhino.FileIO;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

internal sealed class SketchupConfig : IFileTypeConfig
{
  private readonly FileSkpReadOptions _options =
    new()
    {
      JoinEdges = true,
      JoinFaces = true,
      Weld = false,
      ImportCurves = true,
      ImportFacesAsMeshes = true,
      AddObjectsToGroups = false,
      UseGroupLayers = false,
    };

  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    RhinoDoc? doc = RhinoDoc.OpenHeadless(filePath, _options.ToDictionary());
    if (doc is null)
    {
      throw new SpeckleException("Rhino could not import this file");
    }

    // Our GroupUnpacker is very unoptimised for files that contain a lot of instances (like skp imports).
    // This enables a dumb work-around to skip the GroupUnpacker and avoid OOM.
    // SEE https://linear.app/speckle/issue/CNX-3184/skp-file-upload-fails-rhino-importer
    doc.Groups.Clear();
    return doc;
  }

  public void Dispose() { }
}
