using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

public class ArcConverterToHost : ITypedConverter<SOG.Arc, DB.Arc>
{
  private readonly ScalingServiceToHost _scalingService;
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointToXyzConverter;
  private readonly ITypedConverter<SOG.Plane, DB.Plane> _planeConverter;

  public ArcConverterToHost(
    ITypedConverter<SOG.Point, DB.XYZ> pointToXyzConverter,
    ScalingServiceToHost scalingService,
    ITypedConverter<SOG.Plane, DB.Plane> planeConverter
  )
  {
    _pointToXyzConverter = pointToXyzConverter;
    _scalingService = scalingService;
    _planeConverter = planeConverter;
  }

  public DB.Arc Convert(SOG.Arc target)
  {
    // Endpoints coincide, it's a circle.
    if (SOG.Point.Distance(target.startPoint, target.endPoint) < 1E-6)
    {
      double radius =
        target.radius ?? _scalingService.ScaleToNative(target.plane.origin.DistanceTo(target.startPoint), target.units);
      var plane = _planeConverter.Convert(target.plane);
      return DB.Arc.Create(plane, _scalingService.ScaleToNative(radius, target.units), 0, Math.PI * 2);

    }

    return DB.Arc.Create(
      _pointToXyzConverter.Convert(target.startPoint),
      _pointToXyzConverter.Convert(target.endPoint),
      _pointToXyzConverter.Convert(target.midPoint)
    );
  }
}
