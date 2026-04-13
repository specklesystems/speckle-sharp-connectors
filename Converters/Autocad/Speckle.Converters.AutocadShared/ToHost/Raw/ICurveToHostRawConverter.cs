using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.AutocadShared.ToHost.Raw;

public class ICurveToHostRawConverter : ITypedConverter<ICurve, List<(ADB.Entity, Base)>>
{
  private readonly ITypedConverter<SOG.Line, ADB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Arc, ADB.Arc> _arcConverter;
  private readonly ITypedConverter<SOG.Ellipse, ADB.Ellipse> _ellipseConverter;
  private readonly ITypedConverter<SOG.Circle, ADB.Circle> _circleConverter;
  private readonly ITypedConverter<SOG.Polyline, ADB.Polyline3d> _polylineConverter;
  private readonly ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> _polycurveConverter;
  private readonly ITypedConverter<SOG.Curve, ADB.Curve> _curveConverter;

  public ICurveToHostRawConverter(
    ITypedConverter<SOG.Line, ADB.Line> lineConverter,
    ITypedConverter<SOG.Arc, ADB.Arc> arcConverter,
    ITypedConverter<SOG.Ellipse, ADB.Ellipse> ellipseConverter,
    ITypedConverter<SOG.Circle, ADB.Circle> circleConverter,
    ITypedConverter<SOG.Polyline, ADB.Polyline3d> polylineConverter,
    ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> polycurveConverter,
    ITypedConverter<SOG.Curve, ADB.Curve> curveConverter
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _ellipseConverter = ellipseConverter;
    _circleConverter = circleConverter;
    _polylineConverter = polylineConverter;
    _polycurveConverter = polycurveConverter;
    _curveConverter = curveConverter;
  }

  /// <summary>
  /// Converts a given ICurve object to a list of ADB.Curve.
  /// </summary>
  /// <param name="target">The ICurve object to convert.</param>
  /// <returns>The converted list of ADB.Curve.</returns>
  /// <exception cref="NotSupportedException">Thrown when the conversion is not supported for the given type of curve.</exception>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public List<(ADB.Entity, Base)> Convert(ICurve target) =>
    target switch
    {
      SOG.Line line => new() { (_lineConverter.Convert(line), line) },
      SOG.Arc arc => new() { (_arcConverter.Convert(arc), arc) },
      SOG.Circle circle => new() { (_circleConverter.Convert(circle), circle) },
      SOG.Ellipse ellipse => new() { (_ellipseConverter.Convert(ellipse), ellipse) },
      SOG.Polyline polyline => new() { (_polylineConverter.Convert(polyline), polyline) },
      SOG.Curve curve => new() { (_curveConverter.Convert(curve), curve) },
      SOG.Polycurve polycurve => _polycurveConverter.Convert(polycurve),
      _ => throw new ValidationException($"Unable to convert curves of type {target.GetType().Name}"),
    };
}
