using Rhino;
using Rhino.FileIO;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

internal sealed class ObjConfig : IFileTypeConfig
{
  private readonly FileObjReadOptions _readOptions =
    new(new FileReadOptions() { OpenMode = true })
    {
      MapYtoZ = true,
      UseObjGroupsAs = FileObjReadOptions.UseObjGsAs.ObjGroupsAsLayers,
      UseObjObjectsAs = FileObjReadOptions.UseObjOsAs.ObjObjectsAsObjects,
      IgnoreTextures = true, //We don't support MTL uploads, so no point trying to find a MTL file...
    };

  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    var doc = RhinoDoc.CreateHeadless(null);
    try
    {
      if (!FileObj.Read(filePath, doc, _readOptions))
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
