using Speckle.DoubleNumerics;
using TSD.API.Remoting.Geometry;
using TSD.API.Remoting.Structure;

namespace Speckle.Converters.TSDShared;

/// <summary>Outline rings in TSD base units for slab contours and wall panels, shared by both display-value paths.</summary>
internal static class TsdRings
{
  private const double CLOSING_VERTEX_TOLERANCE = 1e-6;

  public static List<Vector3> LiftRing(IPolygon2D polygon, IPlane plane)
  {
    var ring = new List<Vector3>();
    foreach (var vertex in polygon.Vertices)
    {
      var point = plane.Local2Global(vertex.Value);
      ring.Add(new Vector3(point.X, point.Y, point.Z));
    }

    if (ring.Count > 1 && (ring[^1] - ring[0]).Length() < CLOSING_VERTEX_TOLERANCE)
    {
      ring.RemoveAt(ring.Count - 1);
    }

    return ring;
  }

  /// <summary>The panel's quad, bottom edge first and the top edge oriented to close the loop; null without segments.</summary>
  public static List<Vector3>? WallPanelQuad(IStructuralWallPanel panel)
  {
    var bottom = panel.BottomSegment.Value;
    var top = panel.TopSegment.Value;
    if (bottom is null || top is null)
    {
      return null;
    }

    var bottomStart = ToVector(bottom.GetPoint(Location.Start));
    var bottomEnd = ToVector(bottom.GetPoint(Location.End));
    var topA = ToVector(top.GetPoint(Location.Start));
    var topB = ToVector(top.GetPoint(Location.End));

    var (topNearStart, topNearEnd) =
      (topA - bottomStart).Length() <= (topB - bottomStart).Length() ? (topA, topB) : (topB, topA);

    return [bottomStart, bottomEnd, topNearEnd, topNearStart];
  }

  public static Vector3 ToVector(Point3D point) => new(point.X, point.Y, point.Z);
}
