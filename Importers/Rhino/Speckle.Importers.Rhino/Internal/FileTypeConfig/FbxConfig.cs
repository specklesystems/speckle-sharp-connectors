using Rhino;
using Rhino.FileIO;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

public sealed class FbxConfig : IFileTypeConfig
{
  private readonly FileFbxReadOptions _readOptions =
    new()
    {
      MapFbxYtoRhinoZ = true,
      ImportLights = true, // untested, perhaps they come through as points...
      ImportCameras = true,
    };

  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    var doc = RhinoDoc.CreateHeadless(null);
    try
    {
      if (!doc.Import(filePath, _readOptions.ToDictionary()))
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
