using ArcGIS.Core.CIM;

namespace Speckle.Converters.ArcGIS3.Utils;

public static class GeometryUtils
{
  public static bool ValidateMesh(this SOG.Mesh mesh)
  {
    if (mesh.vertices.Count < 3)
    {
      return false;
    }
    else if (mesh.faces.Count < 4)
    {
      return false;
    }
    return true;
  }

  public static int RGBToInt(this CIMRGBColor color)
  {
    return (255 << 24) | ((int)Math.Round(color.R) << 16) | ((int)Math.Round(color.G) << 8) | (int)Math.Round(color.B);
  }

  public static int CIMColorToInt(this CIMColor color)
  {
    return (255 << 24)
      | ((int)Math.Round(color.Values[0]) << 16)
      | ((int)Math.Round(color.Values[1]) << 8)
      | (int)Math.Round(color.Values[2]);
  }

  public static List<double> Values(this SOG.Arc arc)
  {
    List<double> coords =
      new()
      {
        arc.startPoint.x,
        arc.startPoint.y,
        arc.startPoint.z,
        arc.midPoint.x,
        arc.midPoint.y,
        arc.midPoint.z,
        arc.endPoint.x,
        arc.endPoint.y,
        arc.endPoint.z
      };
    return coords;
  }

  public static bool IsClockwisePolygon(this SOG.Polyline polyline)
  {
    bool isClockwise;
    double sum = 0;

    List<SOG.Point> points = polyline.GetPoints();

    if (points.Count < 3)
    {
      throw new ArgumentException("Not enough points for polygon orientation check");
    }
    if (points[0] != points[^1])
    {
      points.Add(points[0]);
    }

    for (int i = 0; i < points.Count - 1; i++)
    {
      sum += (points[i + 1].x - points[i].x) * (points[i + 1].y + points[i].y);
    }
    isClockwise = sum > 0;
    return isClockwise;
  }

  public static bool IsClockwisePolygon(this ACG.Polyline polyline)
  {
    bool isClockwise;
    double sum = 0;

    List<ACG.MapPoint> points = polyline.Points.ToList();

    if (points.Count < 3)
    {
      throw new ArgumentException("Not enough points for polygon orientation check");
    }
    if (points[0] != points[^1])
    {
      points.Add(points[0]);
    }

    for (int i = 0; i < points.Count - 1; i++)
    {
      sum += (points[i + 1].X - points[i].X) * (points[i + 1].Y + points[i].Y);
    }
    isClockwise = sum > 0;
    return isClockwise;
  }

  public static SGIS.GisMultipatchGeometry CompleteMultipatchTriangleStrip(
    this ACG.Multipatch target,
    List<List<SOG.Point>> allPoints,
    int idx
  )
  {
    SGIS.GisMultipatchGeometry multipatch = new();
    List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();

    // get data for this multipatch part
    int ptCount = target.GetPatchPointCount(idx);

    for (int ptIdx = 0; ptIdx < ptCount; ptIdx++)
    {
      if (ptIdx >= 2) // every new point adds a triangle
      {
        multipatch.faces.AddRange(new List<int>() { 3, ptIdx - 2, ptIdx - 1, ptIdx });
        multipatch.vertices.AddRange(pointCoords.GetRange(3 * (ptIdx - 2), 9).ToList());
      }
    }
    return multipatch;
  }

  public static SGIS.GisMultipatchGeometry CompleteMultipatchTriangles(
    this ACG.Multipatch target,
    List<List<SOG.Point>> allPoints,
    int idx
  )
  {
    SGIS.GisMultipatchGeometry multipatch = new();
    List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();

    // get data for this multipatch part
    int ptCount = target.GetPatchPointCount(idx);
    for (int ptIdx = 0; ptIdx < ptCount; ptIdx++)
    {
      var convertedPt = allPoints[idx][ptIdx];
      if (ptIdx >= 2 && (ptIdx + 1) % 3 == 0) // every 3 new points is a new triangle
      {
        multipatch.faces.AddRange(new List<int>() { 3, ptIdx - 2, ptIdx - 1, ptIdx });
        multipatch.vertices.AddRange(pointCoords.GetRange(3 * (ptIdx - 2), 9).ToList());
      }
    }
    return multipatch;
  }

  public static SGIS.GisMultipatchGeometry CompleteMultipatchTriangleFan(
    this ACG.Multipatch target,
    List<List<SOG.Point>> allPoints,
    int idx
  )
  {
    SGIS.GisMultipatchGeometry multipatch = new();
    List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();

    // get data for this multipatch part
    int ptCount = target.GetPatchPointCount(idx);

    for (int ptIdx = 0; ptIdx < ptCount; ptIdx++)
    {
      var convertedPt = allPoints[idx][ptIdx];
      if (ptIdx >= 2) // every new point adds a triangle (originates from 0)
      {
        multipatch.faces.AddRange(new List<int>() { 3, 0, ptIdx - 1, ptIdx });
        multipatch.vertices.AddRange(pointCoords.GetRange(2 * (ptIdx - 2), 6).ToList());
      }
    }
    return multipatch;
  }
}
