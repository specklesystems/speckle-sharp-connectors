using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a MicroStation 2026 COM <see cref="MSIDGN.ComplexShapeElement"/> (compound CLOSED
/// outline made of multiple connected line/arc/curve segments — e.g. a structural member
/// profile with filleted corners) to a Speckle <see cref="Polyline"/> with <c>closed = true</c>.
/// <see cref="MSIDGN.ComplexShapeElement.ConstructVertexList(double)"/> strokes any embedded
/// arcs/curves at the supplied chord tolerance and flattens to a straight-segment vertex list.
/// </summary>
[NameAndRankValue(typeof(MSIDGN.ComplexShapeElement), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ComplexShapeElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  // Chord tolerance for stroking embedded curves. 1mm in master units is fine for visual
  // fidelity in the viewer; tighter values explode the vertex count without much benefit.
  private const double STROKE_TOLERANCE = 0.001;

  public Base Convert(object target) => Convert((MSIDGN.ComplexShapeElement)target);

  private Polyline Convert(MSIDGN.ComplexShapeElement element)
  {
    var s = settingsStore.Current;
    var pts = element.ConstructVertexList(STROKE_TOLERANCE);
    var value = new List<double>(pts.Length * 3);

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
