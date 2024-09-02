using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class PlaneToSpeckleConverter : ITypedConverter<DB.Plane, SOG.Plane>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Vector> _xyzToVectorConverter;

  public PlaneToSpeckleConverter(
    ISettingsStore<RevitConversionSettings> settings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.XYZ, SOG.Vector> xyzToVectorConverter
  )
  {
    _settings = settings;
    _xyzToPointConverter = xyzToPointConverter;
    _xyzToVectorConverter = xyzToVectorConverter;
  }

  public SOG.Plane Convert(DB.Plane target)
  {
    var origin = _xyzToPointConverter.Convert(target.Origin);
    var normal = _xyzToVectorConverter.Convert(target.Normal);
    var xdir = _xyzToVectorConverter.Convert(target.XVec);
    var ydir = _xyzToVectorConverter.Convert(target.YVec);

    return new SOG.Plane
    {
      origin = origin,
      normal = normal,
      xdir = xdir,
      ydir = ydir,
      units = _settings.Current.SpeckleUnits
    };
  }
}
