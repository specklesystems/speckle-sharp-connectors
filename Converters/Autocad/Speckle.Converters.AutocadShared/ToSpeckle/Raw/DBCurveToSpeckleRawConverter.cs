using Speckle.Converters.Common;
using Speckle.Sdk.Models;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBCurveToSpeckleRawConverter(
  ITypedConverter<ADB.Line, SOG.Line> lineConverter,
  ITypedConverter<ADB.Polyline, SOG.Autocad.AutocadPolycurve> polylineConverter,
  ITypedConverter<ADB.Polyline2d, SOG.Autocad.AutocadPolycurve> polyline2dConverter,
  ITypedConverter<ADB.Polyline3d, SOG.Autocad.AutocadPolycurve> polyline3dConverter,
  ITypedConverter<ADB.Arc, SOG.Arc> arcConverter,
  ITypedConverter<ADB.Circle, SOG.Circle> circleConverter,
  ITypedConverter<ADB.Ellipse, SOG.Ellipse> ellipseConverter,
  ITypedConverter<ADB.Spline, SOG.Curve> splineConverter
) : ITypedConverter<ADB.Curve, Objects.ICurve>, ITypedConverter<ADB.Curve, Base>
{
  /// <summary>
  /// Converts an Autocad curve to a Speckle ICurve.
  /// </summary>
  /// <param name="target">The Autocad curve to convert.</param>
  /// <returns>The Speckle curve.</returns>
  /// <remarks>
  /// This is the main converter when the type of curve you input or output does not matter to the caller.<br/>
  /// ⚠️ If an unsupported type of Curve is input, it will be converted as Spline.
  /// </remarks>
  public Result<Objects.ICurve> Convert(ADB.Curve target) =>
    target switch
    {
      ADB.Line line => Success<Objects.ICurve>(lineConverter.Convert(line).Value),
      ADB.Polyline polyline => Success<Objects.ICurve>(polylineConverter.Convert(polyline).Value),
      ADB.Polyline2d polyline2d => Success<Objects.ICurve>(polyline2dConverter.Convert(polyline2d).Value),
      ADB.Polyline3d polyline3d => Success<Objects.ICurve>(polyline3dConverter.Convert(polyline3d).Value),
      ADB.Arc arc => Success<Objects.ICurve>(arcConverter.Convert(arc).Value),
      ADB.Circle circle => Success<Objects.ICurve>(circleConverter.Convert(circle).Value),
      ADB.Ellipse ellipse => Success<Objects.ICurve>(ellipseConverter.Convert(ellipse).Value),
      ADB.Spline spline => Success<Objects.ICurve>(splineConverter.Convert(spline).Value),
      _ => Success<Objects.ICurve>(splineConverter.Convert(target.Spline).Value)
    };

  Result<Base> ITypedConverter<ADB.Curve, Base>.Convert(ADB.Curve target) => Success((Base)Convert(target).Value);
}
