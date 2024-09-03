using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class XyzConversionToPoint : ITypedConverter<DB.XYZ, SOG.Point>
{
  private readonly IScalingServiceToSpeckle _toSpeckleScalingService;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IRevitConversionContextStack _contextStack;

  public XyzConversionToPoint(
    IScalingServiceToSpeckle toSpeckleScalingService,
    IReferencePointConverter referencePointConverter,
    IRevitConversionContextStack contextStack
  )
  {
    _toSpeckleScalingService = toSpeckleScalingService;
    _referencePointConverter = referencePointConverter;
    _contextStack = contextStack;
  }

  public SOG.Point Convert(DB.XYZ target)
  {
    DB.XYZ extPt = _referencePointConverter.ConvertToExternalCoordinates(target, true);

    var pointToSpeckle = new SOG.Point(
      _toSpeckleScalingService.ScaleLength(extPt.X),
      _toSpeckleScalingService.ScaleLength(extPt.Y),
      _toSpeckleScalingService.ScaleLength(extPt.Z),
      _contextStack.Current.SpeckleUnits
    );

    return pointToSpeckle;
  }
}
