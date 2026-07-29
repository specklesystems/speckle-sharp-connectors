using System.Diagnostics.CodeAnalysis;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.AutocadShared.ToHost.Geometry;

/// <summary>
/// A polycurve has segments as list and it can contain different kind of ICurve objects like Arc, Line, Polyline, Curve etc..
/// If polycurve segments are planar and only of type <see cref="SOG.Line"/> and <see cref="SOG.Arc"/>, it can be represented as Polyline in Autocad.
/// A non-planar chain of straight segments becomes a single <see cref="ADB.Polyline3d"/>.
/// Otherwise we convert it as spline (list of ADB.Entity) that switch cases according to each segment type.
/// </summary>
[NameAndRankValue(typeof(SOG.Polycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolycurveToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Polycurve, List<(Entity, Base)>>
{
  private readonly ITypedConverter<SOG.Polycurve, ADB.Polyline> _polylineConverter;
  private readonly ITypedConverter<SOG.Polyline, ADB.Polyline3d> _polyline3dConverter;
  private readonly ITypedConverter<SOG.Line, ADB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Arc, ADB.Arc> _arcConverter;
  private readonly ITypedConverter<SOG.Curve, ADB.Curve> _curveConverter;

  public PolycurveToHostConverter(
    ITypedConverter<SOG.Polycurve, ADB.Polyline> polylineConverter,
    ITypedConverter<SOG.Polyline, ADB.Polyline3d> polyline3dConverter,
    ITypedConverter<SOG.Line, ADB.Line> lineConverter,
    ITypedConverter<SOG.Arc, ADB.Arc> arcConverter,
    ITypedConverter<SOG.Curve, ADB.Curve> curveConverter
  )
  {
    _polylineConverter = polylineConverter;
    _polyline3dConverter = polyline3dConverter;
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _curveConverter = curveConverter;
  }

  public object Convert(Base target) => Convert((SOG.Polycurve)target);

  public List<(Entity, Base)> Convert(SOG.Polycurve target)
  {
    bool convertAsSpline = target.segments.Any(s => s is not SOG.Line and not SOG.Arc);
    bool isPlanar = IsPolycurvePlanar(target);

    if (!convertAsSpline && isPlanar)
    {
      return new() { (_polylineConverter.Convert(target), target) };
    }

    // A non-planar chain of straight segments IS a 3d polyline — bake it as one Polyline3d instead of exploding it into
    // loose lines [ENG-8819]. The 4.0 artefact path has nothing else to go on: SGEO encodes an AutocadPolycurve as a
    // plain Polycurve, so the subtype (and with it AutocadPolycurveToHostConverter) is gone by the time we receive.
    if (!convertAsSpline && TryChainStraightSegments(target, out SOG.Polyline? chain))
    {
      return new() { (_polyline3dConverter.Convert(chain), target) };
    }

    return ConvertAsCurveSegments(target);
  }

  /// <summary>
  /// Flattens a polycurve whose segments are all <see cref="SOG.Line"/>s into the equivalent <see cref="SOG.Polyline"/>,
  /// provided the segments actually chain end-to-start. Returns false for anything else (a gap would be silently bridged
  /// by a polyline, so those keep the per-segment path).
  /// </summary>
  private static bool TryChainStraightSegments(SOG.Polycurve target, [NotNullWhen(true)] out SOG.Polyline? chain)
  {
    chain = null;
    if (target.segments.Count < 2 || target.segments.Any(s => s is not SOG.Line))
    {
      return false;
    }

    var lines = target.segments.Cast<SOG.Line>().ToList();
    // The chain's coordinates come from the segments, so they must all be in one unit for a single Polyline to describe
    // them (SGEO writes a unit per nested segment blob, so don't assume).
    string units = lines[0].units;
    var value = new List<double>((lines.Count + 1) * 3);
    for (int i = 0; i < lines.Count; i++)
    {
      if (!string.Equals(lines[i].units, units, StringComparison.Ordinal))
      {
        return false;
      }
      if (i > 0 && !IsCoincident(lines[i - 1].end, lines[i].start))
      {
        return false;
      }
      value.Add(lines[i].start.x);
      value.Add(lines[i].start.y);
      value.Add(lines[i].start.z);
    }

    var last = lines[^1].end;
    if (target.closed)
    {
      // a closed Polyline omits the repeated last point; bail if the chain doesn't actually come back to the start
      if (!IsCoincident(last, lines[0].start))
      {
        return false;
      }
    }
    else
    {
      value.Add(last.x);
      value.Add(last.y);
      value.Add(last.z);
    }

    chain = new SOG.Polyline
    {
      value = value,
      units = units,
      closed = target.closed,
      domain = target.domain,
    };
    return true;
  }

  // Segment endpoints come off a lossless SGEO/serialization round-trip, so this only absorbs float noise.
  private static bool IsCoincident(SOG.Point a, SOG.Point b) =>
    Math.Abs(a.x - b.x) < TOLERANCE && Math.Abs(a.y - b.y) < TOLERANCE && Math.Abs(a.z - b.z) < TOLERANCE;

  private const double TOLERANCE = 1e-6;

  private bool IsPolycurvePlanar(SOG.Polycurve polycurve)
  {
    double? z = null;
    foreach (Objects.ICurve segment in polycurve.segments)
    {
      switch (segment)
      {
        case SOG.Line o:
          z ??= o.start.z;
          if (o.start.z != z || o.end.z != z)
          {
            return false;
          }

          break;
        case SOG.Arc o:
          z ??= o.startPoint.z;
          if (o.startPoint.z != z || o.midPoint.z != z || o.endPoint.z != z)
          {
            return false;
          }

          break;
        case SOG.Curve o:
          z ??= o.points[2];
          for (int i = 2; i < o.points.Count; i += 3)
          {
            if (o.points[i] != z)
            {
              return false;
            }
          }

          break;
        case SOG.Spiral o:
          z ??= o.startPoint.z;
          if (o.startPoint.z != z || o.endPoint.z != z)
          {
            return false;
          }

          break;
      }
    }
    return true;
  }

  private List<(Entity, Base)> ConvertAsCurveSegments(SOG.Polycurve target)
  {
    // POC: We can improve this once we have IIndex of raw converters and we can get rid of case converters?
    // POC: Should we join entities?
    var converted = new List<(Entity, Base)>();

    foreach (var segment in target.segments)
    {
      switch (segment)
      {
        case SOG.Arc arc:
          converted.Add((_arcConverter.Convert(arc), arc));
          break;
        case SOG.Line line:
          converted.Add((_lineConverter.Convert(line), line));
          break;
        case SOG.Polyline polyline:
          // Polyline3d, NOT the lwpolyline converter: a segment reached here because the polycurve is non-planar, and
          // the implicit Polyline→Polycurve conversion the lwpolyline converter would take flattens it to one plane.
          converted.Add((_polyline3dConverter.Convert(polyline), polyline));
          break;
        case SOG.Curve curve:
          converted.Add((_curveConverter.Convert(curve), curve));
          break;
        case SOG.Spiral spiral:
          // no native Autocad spiral — bake the display polyline rather than dropping the segment [ENG-8819]
          converted.Add((_polyline3dConverter.Convert(spiral.displayValue), spiral));
          break;
        default:
          break;
      }
    }

    return converted;
  }
}
