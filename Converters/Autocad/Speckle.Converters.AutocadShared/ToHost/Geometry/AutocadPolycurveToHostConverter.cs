using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad2023.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Autocad.AutocadPolycurve), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class AutocadPolycurveToHostConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline> _polylineConverter;
  private readonly ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline2d> _polyline2dConverter;
  private readonly ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline3d> _polyline3dConverter;
  private readonly ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> _polycurveConverter;

  public AutocadPolycurveToHostConverter(
    ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline> polylineConverter,
    ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline2d> polyline2dConverter,
    ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline3d> polyline3dConverter,
    ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> polycurveConverter
  )
  {
    _polylineConverter = polylineConverter;
    _polyline2dConverter = polyline2dConverter;
    _polyline3dConverter = polyline3dConverter;
    _polycurveConverter = polycurveConverter;
  }

  public object Convert(Base target)
  {
    SOG.Autocad.AutocadPolycurve polycurve = (SOG.Autocad.AutocadPolycurve)target;

    switch (polycurve.polyType)
    {
      case SOG.Autocad.AutocadPolyType.Light:
        return Has2DValue(polycurve) ? _polycurveConverter.Convert(polycurve) : _polylineConverter.Convert(polycurve);

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

  // Method for backwards compatibility: polylines from 3.10 and before had point2d values in OCS instead of point3d values in WCS/UCS
  private bool Has2DValue(SOG.Autocad.AutocadPolycurve polycurve)
  {
    int pointListCount = polycurve.value.Count;
    if (pointListCount % 3 == 0 && pointListCount % 2 != 0)
    {
      return false;
    }

    if (pointListCount % 2 != 0)
    {
      throw new ValidationException(
        "Polycurve value list was deformed, could not translate into 2d or 3d coordinates."
      );
    }

    int segmentVertexCount = polycurve.closed ? polycurve.segments.Count : polycurve.segments.Count + 1;
    if (pointListCount / 2 == segmentVertexCount)
    {
      return true;
    }

    return false;
  }
}
