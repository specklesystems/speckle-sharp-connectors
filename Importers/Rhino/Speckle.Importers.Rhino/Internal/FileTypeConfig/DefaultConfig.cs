using Rhino;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// Creates a headless doc and imports the file
/// </summary>
/// <remarks>
/// Note: imported geometry will be converted to the default <c>mm</c> units
/// If we need to preserve the file units, a custom config needs to be created
/// </remarks>
public sealed class DefaultConfig : IFileTypeConfig
{
  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    var doc = RhinoDoc.CreateHeadless(null);
    try
    {
      if (!doc.Import(filePath, null))
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
