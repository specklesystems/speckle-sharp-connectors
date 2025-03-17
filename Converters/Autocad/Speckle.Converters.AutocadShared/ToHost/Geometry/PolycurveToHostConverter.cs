using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.AutocadShared.ToHost.Geometry;

/// <summary>
/// A polycurve has segments as list and it can contain different kind of ICurve objects like Arc, Line, Polyline, Curve etc..
/// If polycurve segments are planar and only of type <see cref="SOG.Line"/> and <see cref="SOG.Arc"/>, it can be represented as Polyline in Autocad.
/// Otherwise we convert it as spline (list of ADB.Entity) that switch cases according to each segment type.
/// </summary>
[NameAndRankValue(typeof(SOG.Polycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class PolycurveToHostConverter : IToHostTopLevelConverter, ITypedConverter<SOG.Polycurve, List<(Entity, Base)>>
{
  private readonly ITypedConverter<SOG.Polycurve, ADB.Polyline> _polylineConverter;
  private readonly ITypedConverter<SOG.Line, ADB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Arc, ADB.Arc> _arcConverter;
  private readonly ITypedConverter<SOG.Curve, ADB.Curve> _curveConverter;

  public PolycurveToHostConverter(
    ITypedConverter<SOG.Polycurve, ADB.Polyline> polylineConverter,
    ITypedConverter<SOG.Line, ADB.Line> lineConverter,
    ITypedConverter<SOG.Arc, ADB.Arc> arcConverter,
    ITypedConverter<SOG.Curve, ADB.Curve> curveConverter
  )
  {
    _polylineConverter = polylineConverter;
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _curveConverter = curveConverter;
  }

  public object Convert(Base target) => Convert((SOG.Polycurve)target);

  public List<(Entity, Base)> Convert(SOG.Polycurve target)
  {
    bool convertAsSpline = target.segments.Any(s => s is not SOG.Line and not SOG.Arc);
    bool isPlanar = IsPolycurvePlanar(target);

    if (convertAsSpline || !isPlanar)
    {
      return ConvertAsCurveSegments(target);
    }
    else
    {
      return new() { (_polylineConverter.Convert(target), target) };
    }
  }

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
    var list = new List<ADB.Entity>();

    foreach (var segment in target.segments)
    {
      switch (segment)
      {
        case SOG.Arc arc:
          list.Add(_arcConverter.Convert(arc));
          break;
        case SOG.Line line:
          list.Add(_lineConverter.Convert(line));
          break;
        case SOG.Polyline polyline:
          list.Add(_polylineConverter.Convert(polyline));
          break;
        case SOG.Curve curve:
          list.Add(_curveConverter.Convert(curve));
          break;
        default:
          break;
      }
    }

    return list.Zip(target.segments, (a, b) => ((ADB.Entity)a, (Base)b)).ToList();
  }
}
