using Rhino;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// <see cref="RhinoDoc.OpenHeadless(string)"/> will preserve the units defined by the file
/// </summary>
/// <remarks>
/// For this config to be safe... we need to make sure we're running Rhino 8.24.25251 or greater due to https://mcneel.myjetbrains.com/youtrack/issue/RH-89162
/// </remarks>
public sealed class Rhino3dmConfig : IFileTypeConfig
{
  [Obsolete("Bugged, don't use until fixed")]
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
