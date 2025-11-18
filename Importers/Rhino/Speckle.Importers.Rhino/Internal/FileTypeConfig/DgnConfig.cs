using Rhino;
using Rhino.FileIO;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

internal sealed class DgnConfig : IFileTypeConfig
{
  private readonly FileDgnReadOptions _readOptions = new() { ImportViews = true };

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
