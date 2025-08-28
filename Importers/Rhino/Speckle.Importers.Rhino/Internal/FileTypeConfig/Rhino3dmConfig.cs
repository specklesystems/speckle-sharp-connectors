using Rhino;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// <see cref="RhinoDoc.OpenHeadless(string)"/> will preserve the units defined by the file
/// </summary>
public sealed class Rhino3dmConfig : IFileTypeConfig
{
  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    RhinoDoc? doc = RhinoDoc.OpenHeadless(filePath);
    if (doc is null)
    {
      throw new SpeckleException("Rhino could not open this file");
    }

    return doc;
  }

  public void Dispose() { }
}
