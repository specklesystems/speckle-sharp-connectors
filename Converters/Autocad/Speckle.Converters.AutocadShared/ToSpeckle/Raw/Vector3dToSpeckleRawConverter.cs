using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class Vector3dToSpeckleRawConverter : ITypedConverter<AG.Vector3d, SOG.Vector>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly IReferencePointConverter _referencePointConverter;

  public Vector3dToSpeckleRawConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    IReferencePointConverter referencePointConverter
  )
  {
    _settingsStore = settingsStore;
    _referencePointConverter = referencePointConverter;
  }

  public SOG.Vector Convert(AG.Vector3d target)
  {
    AG.Vector3d extVector = _referencePointConverter.ConvertVectorToExternalCoordinates(target);
    return new(extVector.X, extVector.Y, extVector.Z, _settingsStore.Current.SpeckleUnits);
  }
}
