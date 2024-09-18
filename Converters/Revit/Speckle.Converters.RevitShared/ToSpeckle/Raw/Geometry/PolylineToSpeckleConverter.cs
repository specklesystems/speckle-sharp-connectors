using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class PolylineToSpeckleConverter : ITypedConverter<DB.PolyLine, SOG.Polyline>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;

  public PolylineToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
  }

  public SOG.Polyline Convert(DB.PolyLine target)
  {
    var coords = target.GetCoordinates().SelectMany(coord => _xyzToPointConverter.Convert(coord).ToList()).ToList();
    return new SOG.Polyline { value = coords, units = _converterSettings.Current.SpeckleUnits };
  }
}
