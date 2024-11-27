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

  public static SOG.Mesh CompleteMultipatchTriangleStrip(
    this ACG.Multipatch target,
    List<List<SOG.Point>> allPoints,
    int idx
  )
  {
    List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();
    List<int> faces = new();
    List<double> vertices = new();

    // get data for this multipatch part
    int ptCount = target.GetPatchPointCount(idx);

    for (int ptIdx = 0; ptIdx < ptCount; ptIdx++)
    {
      if (ptIdx >= 2) // every new point adds a triangle
      {
        faces.AddRange(new List<int>() { 3, ptIdx - 2, ptIdx - 1, ptIdx });
        vertices.AddRange(pointCoords.GetRange(3 * (ptIdx - 2), 9).ToList());
      }
    }
    SOG.Mesh multipatch =
      new()
      {
        faces = faces,
        vertices = vertices,
        units = allPoints[idx][0].units
      };
    return multipatch;
  }

  public static SOG.Mesh CompleteMultipatchTriangles(
    this ACG.Multipatch target,
    List<List<SOG.Point>> allPoints,
    int idx
  )
  {
    List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();
    List<int> faces = new();
    List<double> vertices = new();

    // get data for this multipatch part
    int ptCount = target.GetPatchPointCount(idx);
    for (int ptIdx = 0; ptIdx < ptCount; ptIdx++)
    {
      var convertedPt = allPoints[idx][ptIdx];
      if (ptIdx >= 2 && (ptIdx + 1) % 3 == 0) // every 3 new points is a new triangle
      {
        faces.AddRange(new List<int>() { 3, ptIdx - 2, ptIdx - 1, ptIdx });
        vertices.AddRange(pointCoords.GetRange(3 * (ptIdx - 2), 9).ToList());
      }
    }
    SOG.Mesh multipatch =
      new()
      {
        faces = faces,
        vertices = vertices,
        units = allPoints[idx][0].units
      };
    return multipatch;
  }

  public static SOG.Mesh CompleteMultipatchTriangleFan(
    this ACG.Multipatch target,
    List<List<SOG.Point>> allPoints,
    int idx
  )
  {
    List<double> pointCoords = allPoints[idx].SelectMany(x => new List<double>() { x.x, x.y, x.z }).ToList();
    List<int> faces = new();
    List<double> vertices = new();

    // get data for this multipatch part
    int ptCount = target.GetPatchPointCount(idx);

    for (int ptIdx = 0; ptIdx < ptCount; ptIdx++)
    {
      var convertedPt = allPoints[idx][ptIdx];
      if (ptIdx >= 2) // every new point adds a triangle (originates from 0)
      {
        faces.AddRange(new List<int>() { 3, 0, ptIdx - 1, ptIdx });
        vertices.AddRange(pointCoords.GetRange(2 * (ptIdx - 2), 6).ToList());
      }
    }
    SOG.Mesh multipatch =
      new()
      {
        faces = faces,
        vertices = vertices,
        units = allPoints[idx][0].units
      };
    return multipatch;
  }
}
