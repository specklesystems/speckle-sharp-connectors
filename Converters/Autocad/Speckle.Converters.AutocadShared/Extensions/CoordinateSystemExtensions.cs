namespace Speckle.Converters.Autocad.Extensions;

public static class CoordinateSystemExtensions
{
  /// <summary>
  /// Transforms elevation from WCS to UCS.
  /// </summary>
  /// <remarks>
  /// Elevation represents the perpendicular distance of a plane from the origin.
  /// When transforming coordinate systems, this distance must be recalculated
  /// along the transformed normal vector.
  /// </remarks>
  /// <returns>Elevation in UCS</returns>
  public static double TransformElevationToUCS(this AG.Vector3d normal, double elevation, AG.Matrix3d ucsToWcs)
  {
    // get a point on the plane in WCS
    AG.Point3d wcsPoint = AG.Point3d.Origin + normal * elevation;

    // transform to UCS
    AG.Matrix3d wcsToUcs = ucsToWcs.Inverse();
    AG.Point3d ucsPoint = wcsPoint.TransformBy(wcsToUcs);
    AG.Vector3d ucsNormal = normal.TransformBy(wcsToUcs);

    // calculate elevation as perpendicular distance in UCS
    return ucsPoint.GetAsVector().DotProduct(ucsNormal);
  }
}
