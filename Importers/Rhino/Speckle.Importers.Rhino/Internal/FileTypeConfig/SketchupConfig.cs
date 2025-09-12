using Rhino;
using Rhino.FileIO;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

public sealed class SketchupConfig : IFileTypeConfig
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
    var doc = RhinoDoc.CreateHeadless(null);
    try
    {
      if (!doc.Import(filePath, _options.ToDictionary()))
      {
        throw new SpeckleException("Rhino could not import this file");
      }
      return doc;
    }
    catch
    {
      doc.Dispose();
      throw;
    }
  }

  public void Dispose() { }
}
