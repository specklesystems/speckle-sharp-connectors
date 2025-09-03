using Rhino;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

/// <summary>
/// <see cref="RhinoDoc.OpenHeadless(string)"/> will preserve the units defined by the file
/// </summary>
public sealed class Rhino3dmConfig : IFileTypeConfig
{
  [Obsolete("Bugged, don't use until fixed")]
  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    // There is a bug in `OpenHeadless`
    // that creates UI dismissable popups about missing font warnings
    // see https://discourse.mcneel.com/t/rhino-inside-headless-displays-popup-about-missing-fonts/209173/4
    // For this reason, this function should not be used until it's fixed

    RhinoDoc? doc = RhinoDoc.OpenHeadless(filePath);
    if (doc is null)
    {
      throw new SpeckleException("Rhino could not open this file");
    }

    return doc;
  }

  public void Dispose() { }
}
