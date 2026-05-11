using Bentley.GeometryNET;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;
using MgdLine = Bentley.DgnPlatformNET.Elements.LineElement;

namespace Speckle.Converter.MicroStation.ToSpeckle.TopLevel;

/// <summary>
/// Converts a managed <see cref="MgdLine"/> directly into a Speckle <see cref="Line"/>.
/// Geometry comes from the element's <see cref="CurveVector"/>; the first (and only) primitive
/// is a Line whose <see cref="DSegment3d"/> gives us the start/end points in master units.
/// </summary>
public class LineElementConverter(IConverterSettingsStore<MicroStationConversionSettings> settingsStore)
{
  public Base Convert(MgdLine mgdLine)
  {
    var s = settingsStore.Current;
    var applicationId = ((ulong)mgdLine.ElementId).ToString();

    var cv = mgdLine.GetCurveVector();
    if (cv != null)
    {
      var prim = cv.GetPrimitive(0);
      if (prim != null && prim.TryGetLine(out DSegment3d seg))
      {
        return new Line
        {
          start = new Point(seg.StartPoint.X, seg.StartPoint.Y, seg.StartPoint.Z, s.SpeckleUnits),
          end = new Point(seg.EndPoint.X, seg.EndPoint.Y, seg.EndPoint.Z, s.SpeckleUnits),
          units = s.SpeckleUnits,
          applicationId = applicationId,
        };
      }
    }

    // Fallback — shouldn't happen for a real LineElement, but if it does, return a polyline
    // representation rather than throwing.
    return cv != null
      ? CurveVectorToSpeckleHelper.ToSpeckle(cv, s.SpeckleUnits, applicationId)
      : throw new InvalidOperationException($"LineElement {applicationId} has no CurveVector.");
  }
}
