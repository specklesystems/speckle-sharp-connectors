using Speckle.Objects.Geometry;

namespace Speckle.Converters.MicroStation.Extensions;

/// <summary>
/// Extension methods for converting MicroStation 2026 COM coordinate types to Speckle geometry.
/// In MicroStation 2026 the COM <c>Point3d</c> struct contains coordinates already expressed
/// in master units (the COM layer does the UOR-to-master-unit conversion internally).
/// </summary>
public static class DgnPointExtensions
{
  /// <summary>Converts a COM <see cref="MSIDGN.Point3d"/> to a Speckle <see cref="Point"/>.</summary>
  public static Point ToSpecklePoint(this MSIDGN.Point3d pt, string units) => new(pt.X, pt.Y, pt.Z, units);

  /// <summary>Converts a COM <see cref="MSIDGN.Point3d"/> to a flat double array [x, y, z].</summary>
  public static double[] ToArray(this MSIDGN.Point3d pt) => [pt.X, pt.Y, pt.Z];
}
