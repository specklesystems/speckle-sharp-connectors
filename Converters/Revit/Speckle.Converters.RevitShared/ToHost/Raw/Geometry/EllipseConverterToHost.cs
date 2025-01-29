using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

public class EllipseConverterToHost : ITypedConverter<SOG.Ellipse, DB.Curve>
{
  private readonly ScalingServiceToHost _scalingService;
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointToXyzConverter;
  private readonly ITypedConverter<SOG.Plane, DB.Plane> _planeConverter;

  public EllipseConverterToHost(
    ITypedConverter<SOG.Point, DB.XYZ> pointToXyzConverter,
    ScalingServiceToHost scalingService,
    ITypedConverter<SOG.Plane, DB.Plane> planeConverter
  )
  {
    _pointToXyzConverter = pointToXyzConverter;
    _scalingService = scalingService;
    _planeConverter = planeConverter;
  }

  public DB.Curve Convert(SOG.Ellipse target)
  {
    using DB.Plane basePlane = _planeConverter.Convert(target.plane);

    var e = DB.Ellipse.CreateCurve(
      _pointToXyzConverter.Convert(target.plane.origin),
      _scalingService.ScaleToNative(target.firstRadius, target.units),
      _scalingService.ScaleToNative(target.secondRadius, target.units),
      basePlane.XVec.Normalize(),
      basePlane.YVec.Normalize(),
      0,
      2 * Math.PI
    );
    e.MakeBound(target.trimDomain?.start ?? 0, target.trimDomain?.end ?? 2 * Math.PI);
    return e;
  }
}
