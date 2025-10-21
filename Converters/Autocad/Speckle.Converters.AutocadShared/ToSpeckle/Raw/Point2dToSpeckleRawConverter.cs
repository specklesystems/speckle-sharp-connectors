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
    var extPt = _referencePointConverter.ConvertDoublesToExternalCoordinates(new(3) { target.X, target.Y, 0 });

    return new(extPt[0], extPt[1], extPt[2], _settingsStore.Current.SpeckleUnits);
  }
}
