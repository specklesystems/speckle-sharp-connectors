using Rhino;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// Creates a headless doc and imports the file
/// </summary>
/// <remarks>
/// Note: using OpenHeadless should preserve the original file's unit system for file types that have units
/// </remarks>
internal sealed class DefaultConfig : IFileTypeConfig
{
  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    RhinoDoc? doc = RhinoDoc.OpenHeadless(filePath, null);
    if (doc is null)
    {
      throw new SpeckleException("Rhino could not open this file");
    }

    return doc;
  }

  public void Dispose() { }
}
