using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class Point3dToSpeckleRawConverter : ITypedConverter<AG.Point3d, SOG.Point>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly IReferencePointConverter _referencePointConverter;

  public Point3dToSpeckleRawConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    IReferencePointConverter referencePointConverter
  )
  {
    _settingsStore = settingsStore;
    _referencePointConverter = referencePointConverter;
  }

  public SOG.Point Convert(AG.Point3d target)
  {
    AG.Point3d extPt = _referencePointConverter.ConvertWCSPointToExternalCoordinates(target);
    return new(extPt.X, extPt.Y, extPt.Z, _settingsStore.Current.SpeckleUnits);
  }
}
