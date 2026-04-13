using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class LocationToSpeckleConverter : ITypedConverter<DB.Location, Base>
{
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzConverter;

  public LocationToSpeckleConverter(
    ITypedConverter<DB.Curve, ICurve> curveConverter,
    ITypedConverter<DB.XYZ, SOG.Point> xyzConverter
  )
  {
    _curveConverter = curveConverter;
    _xyzConverter = xyzConverter;
  }

  public Base Convert(DB.Location target)
  {
    return target switch
    {
      DB.LocationCurve curve => (_curveConverter.Convert(curve.Curve) as Base)!, // POC: ICurve and Base are not related but we know they must be, had to soft cast and then !.
      DB.LocationPoint point => _xyzConverter.Convert(point.Point),
      _ => throw new ValidationException($"Unexpected location type {target.GetType()}"),
    };
  }
}
