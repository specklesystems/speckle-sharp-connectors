using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class Point2dToSpeckleRawConverter : ITypedConverter<AG.Point2d, SOG.Point>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly IReferencePointConverter _referencePointConverter;

  public Point2dToSpeckleRawConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    IReferencePointConverter referencePointConverter
  )
  {
    _settingsStore = settingsStore;
    _referencePointConverter = referencePointConverter;
  }

  public SOG.Point Convert(AG.Point2d target)
  {
    AG.Point3d extPt = _referencePointConverter.ConvertPointToExternalCoordinates(
      new AG.Point3d(target.X, target.Y, 0)
    );

    return new(extPt.X, extPt.Y, extPt.Z, _settingsStore.Current.SpeckleUnits);
  }
}
