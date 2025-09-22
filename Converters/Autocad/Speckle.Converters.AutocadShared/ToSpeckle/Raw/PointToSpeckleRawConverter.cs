using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class PointToSpeckleRawConverter : ITypedConverter<AG.Point3d, SOG.Point>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly IReferencePointConverter _referencePointConverter;

  public PointToSpeckleRawConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    IReferencePointConverter referencePointConverter
  )
  {
    _settingsStore = settingsStore;
    _referencePointConverter = referencePointConverter;
  }

  public SOG.Point Convert(AG.Point3d target)
  {
    AG.Point3d extPt = _referencePointConverter.ConvertPointToExternalCoordinates(target);
    return new(extPt.X, extPt.Y, extPt.Z, _settingsStore.Current.SpeckleUnits);
  }
}
