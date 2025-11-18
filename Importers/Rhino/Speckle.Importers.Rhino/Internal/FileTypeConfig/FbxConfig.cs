using Rhino;
using Rhino.FileIO;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

internal sealed class FbxConfig : IFileTypeConfig
{
  private readonly FileFbxReadOptions _readOptions =
    new()
    {
      MapFbxYtoRhinoZ = true,
      ImportLights = false, // Speckle doesn't support LightObject s
      ImportCameras = true,
    };

  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    RhinoDoc? doc = RhinoDoc.OpenHeadless(filePath, _readOptions.ToDictionary());
    if (doc is null)
    {
      throw new SpeckleException("Rhino could not open this file");
    }
    return doc;
  }

  public void Dispose() { }
}
