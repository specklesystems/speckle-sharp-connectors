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
      AddObjectsToGroups = true,
      ImportCurves = true,
      ImportFacesAsMeshes = true,
      UseGroupLayers = false, //Our GroupUnpacker is very unoptimised for files that contain a lot of instances (like skp imports). This enables a dumb work-around to skip the GroupUnpacker and avoid OOM.
    };

  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    RhinoDoc? doc = RhinoDoc.OpenHeadless(filePath, _options.ToDictionary());
    if (doc is null)
    {
      throw new SpeckleException("Rhino could not import this file");
    }
    return doc;
  }

  public void Dispose() { }
}
