using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.ShapeElement"/> (closed planar polygon —
/// floor outline, profile, region boundary) to a Speckle <see cref="Polyline"/> with
/// <c>closed = true</c>. <see cref="MSIDGN.ShapeElement.GetVertices"/> returns the corner
/// points in master units; the polygon is implicitly closed (last vertex back to first), so
/// we mark the polyline closed and don't repeat the start point.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.ShapeElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ShapeElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public Base Convert(object target) => Convert((MSIDGN.ShapeElement)target);

  private Polyline Convert(MSIDGN.ShapeElement element)
  {
    var s = settingsStore.Current;
    var pts = element.GetVertices();
    var value = new List<double>(pts.Length * 3);

    // ShapeElement.GetVertices() typically returns N+1 points (last == first) to encode the
    // closure; strip the duplicate so the Polyline.closed flag is the single source of truth.
    int count = pts.Length;
    if (count >= 2)
    {
      var first = pts[0];
      var last = pts[count - 1];
      const double EPS = 1e-9;
      if (
        Math.Abs(first.X - last.X) < EPS
        && Math.Abs(first.Y - last.Y) < EPS
        && Math.Abs(first.Z - last.Z) < EPS
      )
      {
        count--;
      }
    }

    for (int i = 0; i < count; i++)
    {
      value.Add(pts[i].X);
      value.Add(pts[i].Y);
      value.Add(pts[i].Z);
    }

    return new Polyline
    {
      value = value,
      closed = true,
      units = s.SpeckleUnits,
      applicationId = element.ID.ToString(),
    };
  }
}
