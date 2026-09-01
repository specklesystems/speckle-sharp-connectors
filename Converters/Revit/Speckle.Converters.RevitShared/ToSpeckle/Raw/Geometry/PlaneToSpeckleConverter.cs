using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class PlaneToSpeckleConverter : ITypedConverter<DB.Plane, SOG.Plane>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Vector> _xyzToVectorConverter;

  public PlaneToSpeckleConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.XYZ, SOG.Vector> xyzToVectorConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _xyzToVectorConverter = xyzToVectorConverter;
  }

  public SOG.Plane Convert(DB.Plane target)
  {
    var origin = _xyzToPointConverter.Convert(target.Origin);
    var normal = _xyzToVectorConverter.Convert(target.Normal);
    var xdir = _xyzToVectorConverter.Convert(target.XVec);
    var ydir = _xyzToVectorConverter.Convert(target.YVec);

    // A plane's basis vectors are pure directions and MUST be unit length. VectorToSpeckleConverter applies length
    // scaling (Revit feet -> document units, e.g. x304.8), which leaves them non-unitized — a plane that is not "true".
    // Downstream consumers that read the plane's basis magnitude (e.g. arc.plane in the SGEO/viewer path) then
    // mis-scale the geometry, so the arc renders at the wrong size. Normalize to restore unit basis vectors.
    normal.Normalize();
    xdir.Normalize();
    ydir.Normalize();

    return new SOG.Plane
    {
      origin = origin,
      normal = normal,
      xdir = xdir,
      ydir = ydir,
      units = _converterSettings.Current.SpeckleUnits,
    };
  }
}
