using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class VectorToSpeckleConverter : ITypedConverter<DB.XYZ, SOG.Vector>
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IRevitConversionContextStack _contextStack;

  public VectorToSpeckleConverter(
    IReferencePointConverter referencePointConverter,
    ScalingServiceToSpeckle scalingService,
    IRevitConversionContextStack contextStack
  )
  {
    _referencePointConverter = referencePointConverter;
    _scalingService = scalingService;
    _contextStack = contextStack;
  }

  public SOG.Vector Convert(DB.XYZ target)
  {
    // POC: originally had a concept of not transforming, but this was
    // optional arg defaulting to false - removing the argument appeared to break nothing
    DB.XYZ extPt = _referencePointConverter.ConvertToExternalCoordinates(target, false);
    var pointToSpeckle = new SOG.Vector(
      _scalingService.ScaleLength(extPt.X),
      _scalingService.ScaleLength(extPt.Y),
      _scalingService.ScaleLength(extPt.Z),
      _contextStack.Current.SpeckleUnits
    );

    return pointToSpeckle;
  }
}
