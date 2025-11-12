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
      UseGroupLayers = true,
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
