using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class ICurveConverterToHost : ITypedConverter<ICurve, DB.CurveArray>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly ITypedConverter<SOG.Vector, DB.XYZ> _vectorConverter;
  private readonly ITypedConverter<SOG.Arc, DB.Arc> _arcConverter;
  private readonly ITypedConverter<SOG.Line, DB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Circle, DB.Arc> _circleConverter;
  private readonly ITypedConverter<SOG.Ellipse, DB.Curve> _ellipseConverter;
  private readonly ITypedConverter<SOG.Polyline, DB.CurveArray> _polylineConverter;
  private readonly ITypedConverter<SOG.Curve, DB.Curve> _curveConverter;

  public ICurveConverterToHost(
    ITypedConverter<SOG.Point, DB.XYZ> pointConverter,
    ITypedConverter<SOG.Vector, DB.XYZ> vectorConverter,
    ITypedConverter<SOG.Arc, DB.Arc> arcConverter,
    ITypedConverter<SOG.Line, DB.Line> lineConverter,
    ITypedConverter<SOG.Circle, DB.Arc> circleConverter,
    ITypedConverter<SOG.Ellipse, DB.Curve> ellipseConverter,
    ITypedConverter<SOG.Polyline, DB.CurveArray> polylineConverter,
    ITypedConverter<SOG.Curve, DB.Curve> curveConverter
  )
  {
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _arcConverter = arcConverter;
    _lineConverter = lineConverter;
    _circleConverter = circleConverter;
    _ellipseConverter = ellipseConverter;
    _polylineConverter = polylineConverter;
    _curveConverter = curveConverter;
  }

  public DB.CurveArray Convert(ICurve target)
  {
    DB.CurveArray curveArray = new();
    switch (target)
    {
      case SOG.Line line:
        curveArray.Append(_lineConverter.Convert(line));
        return curveArray;

      case SOG.Arc arc:
        curveArray.Append(_arcConverter.Convert(arc));
        return curveArray;

      case SOG.Circle circle:
        curveArray.Append(_circleConverter.Convert(circle));
        return curveArray;

      case SOG.Ellipse ellipse:
        curveArray.Append(_ellipseConverter.Convert(ellipse));
        return curveArray;

      case SOG.Spiral spiral:
        return _polylineConverter.Convert(spiral.displayValue);

      case SOG.Curve nurbs:
        if (nurbs.closed) // NOTE: ensure we always nicely convert cyclical curves
        {
          return _polylineConverter.Convert(nurbs.displayValue);
        }
        var n = _curveConverter.Convert(nurbs);
        curveArray.Append(n);
        return curveArray;

      case SOG.Polyline poly:
        return _polylineConverter.Convert(poly);

      case SOG.Polycurve plc:
        foreach (var seg in plc.segments)
        {
          // Enumerate all curves in the array to ensure polylines get fully converted.
          using var subCurves = Convert(seg);
          foreach (DB.Curve curve in subCurves)
          {
            curveArray.Append(curve);
          }
        }
        return curveArray;
      default:
        throw new ValidationException($"The provided geometry of type {target.GetType()} is not a supported");
    }
  }
}
