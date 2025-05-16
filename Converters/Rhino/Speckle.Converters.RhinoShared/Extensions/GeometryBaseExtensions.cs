namespace Speckle.Converters.Rhino.Extensions;

public static class GeometryBaseExtensions
{
  /// <summary>
  /// Getting translation vector from origin to the Geometry bbox Center (if geometry is far from origin and translation needed)
  /// </summary>
  /// <returns>
  /// Vector from origin to Geometry bbox center (if translation needed), otherwise zero-length vector.
  /// </returns>
  public static bool IsFarFromOrigin(this RG.GeometryBase geometry, out RG.Vector3d vectorToGeometry)
  {
    var geometryBbox = geometry.GetBoundingBox(false); // 'false' for 'accurate' parameter to accelerate bbox calculation
    if (geometryBbox.Min.DistanceTo(RG.Point3d.Origin) > 1e5 || geometryBbox.Max.DistanceTo(RG.Point3d.Origin) > 1e5)
    {
      vectorToGeometry = new RG.Vector3d(geometryBbox.Center);
      return true;
    }

    vectorToGeometry = new RG.Vector3d();
    return false;
  }
}
