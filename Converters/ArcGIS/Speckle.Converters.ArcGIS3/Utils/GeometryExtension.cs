using ArcGIS.Core.CIM;

namespace Speckle.Converters.ArcGIS3.Utils;

public static class GeometryUtils
{
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
}
