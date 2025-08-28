using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Sdk;

namespace Speckle.Importers.Rhino.Internal.FileTypeConfig;

public sealed class SketchupConfig : IFileTypeConfig
{
  private readonly DefaultConfig _defaultConfig = new();

  public RhinoDoc OpenInHeadlessDocument(string filePath)
  {
    var doc = RhinoDoc.CreateHeadless(null);
    try
    {
      if (!doc.Import(filePath, null))
      {
        throw new SpeckleException("Rhino could not import this file");
      }
      PreProcessDocument(doc);
      return doc;
    }
    catch
    {
      doc.Dispose();
      throw;
    }
  }

  /// <summary>
  /// Clean up step to strip imported meshes of their NGon data, leaving only the triangle/quad data behind.
  /// This works around a bug in the sketchup importer creating invalid ngons.
  /// https://discourse.mcneel.com/t/meshes-imported-from-skp-file-have-invalid-non-cww-ngons/208028
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
  private static void PreProcessDocument(RhinoDoc doc)
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

  public void Dispose() => _defaultConfig.Dispose();
}
