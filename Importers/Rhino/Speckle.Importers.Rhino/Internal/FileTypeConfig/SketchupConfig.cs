using Rhino;
using Rhino.Collections;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

public sealed class SketchupConfig : IFileTypeConfig
{
  public ArchivableDictionary? ImportOptions => null;

  /// <summary>
  /// Clean up step to strip imported meshes of their NGon data, leaving only the triangle/quad data behind.
  /// This works around a bug in the sketchup importer creating invalid ngons.
  /// </summary>
  /// <remarks>
  /// Without this cleanup step, skp imports send incorrect meshes to speckle
  /// I believe there is a bug in Rhino's skp file importing logic
  /// The <see cref="MeshNgon.BoundaryVertexIndexList()"/> function documents that it will return ccw faces,
  /// and this holds true for native modeled rhino meshes, but not meshes from sketchup imports.
  /// Since Speckle's conversions rely on this function returning how it's documented, the resulting speckle geometry
  /// would be invalid without this step
  /// </remarks>
  /// <param name="doc"></param>
  public void PreProcessDocument(RhinoDoc doc)
  {
    // Process regular meshes in the document
    foreach (var obj in doc.Objects.GetObjectList(ObjectType.Mesh))
    {
      if (obj.Geometry is not Mesh mesh)
      {
        continue;
      }

      if (mesh.Ngons.Count <= 0)
      {
        continue;
      }

      mesh.Ngons.Clear();
      _ = doc.Objects.Replace(obj.Id, mesh);
    }

    //TODO: same for meshes inside blocks
  }
}
