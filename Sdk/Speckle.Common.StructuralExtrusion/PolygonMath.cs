using Speckle.DoubleNumerics;

namespace Speckle.Common.StructuralExtrusion;

internal static class PolygonMath
{
  public const double AREA_EPSILON = 1e-12;

  public static double SignedArea(IReadOnlyList<Vector2> polygon)
  {
    double sum = 0;
    for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
    {
      sum += polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
    }
    return sum / 2;
  }

  public static Vector2 Centroid(IReadOnlyList<Vector2> polygon, double signedArea)
  {
    double cx = 0,
      cy = 0;
    for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
    {
      double cross = polygon[j].X * polygon[i].Y - polygon[i].X * polygon[j].Y;
      cx += (polygon[j].X + polygon[i].X) * cross;
      cy += (polygon[j].Y + polygon[i].Y) * cross;
    }
    return new Vector2(cx / (6 * signedArea), cy / (6 * signedArea));
  }

  public static List<Vector2> WithWinding(IReadOnlyList<Vector2> polygon, bool counterClockwise)
  {
    var result = new List<Vector2>(polygon);
    if (SignedArea(polygon) > 0 != counterClockwise)
    {
      result.Reverse();
    }
    return result;
  }

  public static List<Vector2> Translated(IReadOnlyList<Vector2> polygon, Vector2 offset)
  {
    var result = new List<Vector2>(polygon.Count);
    foreach (var p in polygon)
    {
      result.Add(p + offset);
    }
    return result;
  }

  public static Vector3 NewellNormal(IReadOnlyList<Vector3> polygon)
  {
    double nx = 0,
      ny = 0,
      nz = 0;
    for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
    {
      var a = polygon[j];
      var b = polygon[i];
      nx += (a.Y - b.Y) * (a.Z + b.Z);
      ny += (a.Z - b.Z) * (a.X + b.X);
      nz += (a.X - b.X) * (a.Y + b.Y);
    }
    return new Vector3(nx, ny, nz);
  }
}
