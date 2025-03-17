using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class VectorToSpeckleConverter : ITypedConverter<DB.XYZ, SOG.Vector>
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _settingsStore;

  public VectorToSpeckleConverter(
    IReferencePointConverter referencePointConverter,
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
    _scalingService = scalingService;
    _settingsStore = settingsStore;
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
      _settingsStore.Current.SpeckleUnits
    );

    return pointToSpeckle;
  }
}
