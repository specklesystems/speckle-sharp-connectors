using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.AutocadShared.ToHost.Raw;

public class ICurveToHostRawConverter : ITypedConverter<ICurve, ADB.Curve>
{
  private readonly ITypedConverter<SOG.Line, ADB.Line> _lineConverter;
  private readonly ITypedConverter<SOG.Arc, ADB.Arc> _arcConverter;
  private readonly ITypedConverter<SOG.Ellipse, ADB.Ellipse> _ellipseConverter;
  private readonly ITypedConverter<SOG.Circle, ADB.Circle> _circleConverter;
  private readonly ITypedConverter<SOG.Polyline, ADB.Polyline3d> _polylineConverter;
  private readonly ITypedConverter<SOG.Curve, ADB.Curve> _curveConverter;

  public ICurveToHostRawConverter(
    ITypedConverter<SOG.Line, ADB.Line> lineConverter,
    ITypedConverter<SOG.Arc, ADB.Arc> arcConverter,
    ITypedConverter<SOG.Ellipse, ADB.Ellipse> ellipseConverter,
    ITypedConverter<SOG.Circle, ADB.Circle> circleConverter,
    ITypedConverter<SOG.Polyline, ADB.Polyline3d> polylineConverter,
    ITypedConverter<SOG.Curve, ADB.Curve> curveConverter
  )
  {
    _lineConverter = lineConverter;
    _arcConverter = arcConverter;
    _ellipseConverter = ellipseConverter;
    _circleConverter = circleConverter;
    _polylineConverter = polylineConverter;
    _curveConverter = curveConverter;
  }

  /// <summary>
  /// Converts a given ICurve object to a list of ADB.Curve.
  /// </summary>
  /// <param name="target">The ICurve object to convert.</param>
  /// <returns>The converted list of ADB.Curve.</returns>
  /// <exception cref="NotSupportedException">Thrown when the conversion is not supported for the given type of curve.</exception>
  /// <remarks>⚠️ This conversion does NOT perform scaling.</remarks>
  public ADB.Curve Convert(ICurve target) =>
    target switch
    {
      SOG.Line line => _lineConverter.Convert(line),
      SOG.Arc arc => _arcConverter.Convert(arc),
      SOG.Circle circle => _circleConverter.Convert(circle),
      SOG.Ellipse ellipse => _ellipseConverter.Convert(ellipse),
      SOG.Polyline polyline => _polylineConverter.Convert(polyline),
      SOG.Curve curve => _curveConverter.Convert(curve),
      SOG.Polycurve
        => throw new ConversionException($"Use direct Polycurve converter for type {target.GetType().Name}"),
      _ => throw new ValidationException($"Unable to convert curves of type {target.GetType().Name}")
    };
}
