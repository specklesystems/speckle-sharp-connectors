using Rhino;
using Rhino.Collections;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

public sealed class DefaultConfig : IFileTypeConfig
{
  public ArchivableDictionary? ImportOptions => null;

  public void PreProcessDocument(RhinoDoc doc) { }
}
