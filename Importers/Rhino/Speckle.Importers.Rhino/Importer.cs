using Rhino.Collections;
using Rhino.FileIO;

namespace Speckle.Importers.Rhino;

public static class Importer
{
  public static ArchivableDictionary GetOptions(string extension) =>
    extension.ToLowerInvariant() switch
    {
      ".skp" => new FileSkpReadOptions
      {
        ImportFacesAsMeshes = true,
        JoinEdges = true,
        JoinFaces = true,
        Weld = false,
        AddObjectsToGroups = true,
        EmbedTexturesInModel = true
      }.ToDictionary(),
      _ => new ArchivableDictionary(),
    };
}
