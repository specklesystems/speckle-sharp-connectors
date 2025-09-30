using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DoublesToSpeckleRawConverter : ITypedConverter<List<double>, SOG.Polyline>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;
  private readonly IReferencePointConverter _referencePointConverter;

  public DoublesToSpeckleRawConverter(
    IConverterSettingsStore<AutocadConversionSettings> settingsStore,
    IReferencePointConverter referencePointConverter
  )
  {
    _settingsStore = settingsStore;
    _referencePointConverter = referencePointConverter;
  }

  public SOG.Polyline Convert(List<double> target)
  {
    // throw if list is malformed
    if (target.Count % 3 != 0)
    {
      throw new ArgumentException("Point list of xyz values is malformed", nameof(target));
    }

    List<double> value = _referencePointConverter.ConvertDoublesToExternalCoordinates(target);

    return new() { value = value, units = _settingsStore.Current.SpeckleUnits };
  }
}
