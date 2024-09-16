using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Objects.Primitive;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class LineConversionToSpeckle : ITypedConverter<DB.Line, SOG.Line>
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ScalingServiceToSpeckle _scalingService;

  public LineConversionToSpeckle(
    IRevitConversionContextStack contextStack,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ScalingServiceToSpeckle scalingService
  )
  {
    _contextStack = contextStack;
    _xyzToPointConverter = xyzToPointConverter;
    _scalingService = scalingService;
  }

  public SOG.Line Convert(DB.Line target) =>
    new()
    {
      units = _contextStack.Current.SpeckleUnits,
      start = _xyzToPointConverter.Convert(target.GetEndPoint(0)),
      end = _xyzToPointConverter.Convert(target.GetEndPoint(1)),
      domain = new Interval { start = target.GetEndParameter(0), end = target.GetEndParameter(1) },
    };
}
