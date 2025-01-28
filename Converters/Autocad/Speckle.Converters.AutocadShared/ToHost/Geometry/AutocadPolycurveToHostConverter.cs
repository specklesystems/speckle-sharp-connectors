using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry.Autocad;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad2023.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Autocad.AutocadPolycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class AutocadPolycurveToHostConverter : ITypedConverter<SOG.Autocad.AutocadPolycurve, object>
{
  private readonly ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline> _polylineConverter;
  private readonly ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline2d> _polyline2dConverter;
  private readonly ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline3d> _polyline3dConverter;

  public AutocadPolycurveToHostConverter(
    ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline> polylineConverter,
    ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline2d> polyline2dConverter,
    ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline3d> polyline3dConverter
  )
  {
    _polylineConverter = polylineConverter;
    _polyline2dConverter = polyline2dConverter;
    _polyline3dConverter = polyline3dConverter;
  }

  public object Convert(AutocadPolycurve polycurve)
  {
    switch (polycurve.polyType)
    {
      case SOG.Autocad.AutocadPolyType.Light:
        return _polylineConverter.Convert(polycurve);

      case SOG.Autocad.AutocadPolyType.Simple2d:
      case SOG.Autocad.AutocadPolyType.FitCurve2d:
      case SOG.Autocad.AutocadPolyType.CubicSpline2d:
      case SOG.Autocad.AutocadPolyType.QuadSpline2d:
        return _polyline2dConverter.Convert(polycurve);

      case SOG.Autocad.AutocadPolyType.Simple3d:
      case SOG.Autocad.AutocadPolyType.CubicSpline3d:
      case SOG.Autocad.AutocadPolyType.QuadSpline3d:
        return _polyline3dConverter.Convert(polycurve);

      default:
        throw new ValidationException("Unknown poly type for AutocadPolycurve");
    }
  }
}
