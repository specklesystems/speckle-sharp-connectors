using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class XyzConversionToPoint : ITypedConverter<DB.XYZ, SOG.Point>
{
  private readonly IScalingServiceToSpeckle _toSpeckleScalingService;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly ISettingsStore<RevitConversionSettings> _settings;

  public XyzConversionToPoint(
    IScalingServiceToSpeckle toSpeckleScalingService,
    IReferencePointConverter referencePointConverter,
    ISettingsStore<RevitConversionSettings> settings
  )
  {
    _toSpeckleScalingService = toSpeckleScalingService;
    _referencePointConverter = referencePointConverter;
    _settings = settings;
  }

  public SOG.Point Convert(DB.XYZ target)
  {
    DB.XYZ extPt = _referencePointConverter.ConvertToExternalCoordinates(target, true);

    var pointToSpeckle = new SOG.Point(
      _toSpeckleScalingService.ScaleLength(extPt.X),
      _toSpeckleScalingService.ScaleLength(extPt.Y),
      _toSpeckleScalingService.ScaleLength(extPt.Z),
      _settings.Current.SpeckleUnits
    );

    return pointToSpeckle;
  }
}
