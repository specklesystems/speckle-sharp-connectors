using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class PolylineToSpeckleConverter : ITypedConverter<DB.PolyLine, SOG.Polyline>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;

  public PolylineToSpeckleConverter(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter
  )
  {
    _settings = settings;
    _xyzToPointConverter = xyzToPointConverter;
  }

  public SOG.Polyline Convert(DB.PolyLine target)
  {
    var coords = target.GetCoordinates().SelectMany(coord => _xyzToPointConverter.Convert(coord).ToList()).ToList();
    return new SOG.Polyline { value = coords, units = _settings.Current.SpeckleUnits };
  }
}
