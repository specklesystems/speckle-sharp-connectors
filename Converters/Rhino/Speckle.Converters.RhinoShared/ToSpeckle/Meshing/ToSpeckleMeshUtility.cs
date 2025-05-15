using Speckle.Converters.Common.Objects;
using Speckle.DoubleNumerics;

namespace Speckle.Converters.Rhino.ToSpeckle.Meshing;

public static class ToSpeckleMeshUtility
{
  /// <summary>
  /// Extracting Rhino Mesh from Rhino GeometryBase and converting to Speckle with high accuracy, considering distance from origin and geometry topology.
  /// </summary>
  /// <returns>
  /// Converted Speckle meshes.
  /// </returns>
  public static List<SOG.Mesh> GetSpeckleMeshes(
    RG.GeometryBase geometry,
    bool modelFarFromOrigin,
    string units,
    ITypedConverter<RG.Mesh, SOG.Mesh> meshConverter // not pretty
  )
  {
    // 1. If needed, move geometry to origin before all operations and extract Rhino Mesh
    RG.Mesh displayMesh = GetDisplayMeshWithOriginalPositionVector(
      geometry,
      modelFarFromOrigin,
      out RG.Vector3d? vectorToGeometry
    );

    // 2. Convert extracted Mesh to Speckle. We don't move geometry back yet, because 'far from origin' geometry is causing Speckle conversion issues too
    List<SOG.Mesh> displayValue = new() { meshConverter.Convert(displayMesh) };

    // 3. Move Speckle geometry back from origin, if translation was applied
    if (vectorToGeometry is RG.Vector3d vector)
    {
      Matrix4x4 matrix = new(1, 0, 0, vector.X, 0, 1, 0, vector.Y, 0, 0, 1, vector.Z, 0, 0, 0, 1);
      SO.Transform transform = new() { matrix = matrix, units = units };
      displayValue.ForEach(x => x.Transform(transform));
    }

    return displayValue;
  }

  private static RG.Mesh GetDisplayMeshWithOriginalPositionVector(
    RG.GeometryBase geometry,
    bool modelFarFromOrigin,
    out RG.Vector3d? vectorToOriginalGeometry
  )
  {
    vectorToOriginalGeometry = null;

    // 1.1. General check: if Model is NOT far from origin (99% of Rhino models): extract meshes as usual
    if (!modelFarFromOrigin)
    {
      return DisplayMeshExtractor.GetGeometryDisplayMesh(geometry, true);
    }
    // 1.2. Geometry check: if the model extent is far from origin, but object itself is NOT far from origin: extract meshes as usual
    if (!TryGetTranslationVector(geometry, out RG.Vector3d vectorToGeometry))
    {
      return DisplayMeshExtractor.GetGeometryDisplayMesh(geometry, true);
    }
    // 1.3. If the object is far from origin and risking faulty meshes due to precision errors: duplicate geometry and move it to origin
    RG.GeometryBase geometryToMesh = geometry.Duplicate();
    geometryToMesh.Transform(RG.Transform.Translation(-vectorToGeometry));

    vectorToOriginalGeometry = vectorToGeometry;
    return DisplayMeshExtractor.GetGeometryDisplayMesh(geometryToMesh, true);
  }

  /// <summary>
  /// Getting translation vector from origin to the Geometry bbox Center (if geometry is far from origin and translation needed)
  /// </summary>
  /// <returns>
  /// True and the vector from origin to Geometry bbox center (if translation needed), otherwise false and zero-length vector.
  /// </returns>
  private static bool TryGetTranslationVector(RG.GeometryBase geom, out RG.Vector3d vector)
  {
    vector = new RG.Vector3d();
    var geometryBbox = geom.GetBoundingBox(false); // 'false' for 'accurate' parameter to accelerate bbox calculation
    if (geometryBbox.Min.DistanceTo(RG.Point3d.Origin) > 1e6 || geometryBbox.Max.DistanceTo(RG.Point3d.Origin) > 1e6)
    {
      vector = new RG.Vector3d(geometryBbox.Center);
      return true;
    }

    return false;
  }
}
