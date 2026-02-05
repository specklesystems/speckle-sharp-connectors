using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

/// <summary>
/// Converts curve origin plane components directly to Speckle plane.
/// </summary>
/// <remarks>
/// <para>
/// Revit's <see cref="DB.Plane.CreateByOriginAndBasis"/> fails when the origin is ~10+ km from the internal origin,
/// even though the documented limit is 16 km. This is a known API limitation:
/// https://forums.autodesk.com/t5/revit-api-forum/the-input-point-lies-outside-of-revit-design-limits/td-p/12689066
///</para>
/// <para>
/// This converter bypasses Revit plane creation entirely and builds Speckle planes directly from
/// the curve's origin and basis vectors.
/// </para>
/// </remarks>
public class CurveOriginToPlaneConverter
  : ITypedConverter<(DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal), SOG.Plane>
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.XYZ, SOG.Point> _xyzToPointConverter;
  private readonly ITypedConverter<DB.XYZ, SOG.Vector> _xyzToVectorConverter;
  private readonly ITypedConverter<DB.Plane, SOG.Plane> _planeConverter;

  public CurveOriginToPlaneConverter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<DB.XYZ, SOG.Point> xyzToPointConverter,
    ITypedConverter<DB.XYZ, SOG.Vector> xyzToVectorConverter,
    ITypedConverter<DB.Plane, SOG.Plane> planeConverter
  )
  {
    _converterSettings = converterSettings;
    _xyzToPointConverter = xyzToPointConverter;
    _xyzToVectorConverter = xyzToVectorConverter;
    _planeConverter = planeConverter;
  }

  public SOG.Plane Convert((DB.XYZ origin, DB.XYZ xDir, DB.XYZ yDir, DB.XYZ normal) target)
  {
    // within limits? then use standard Revit plane creation
    if (DB.XYZ.IsWithinLengthLimits(target.origin))
    {
      using var revitPlane = DB.Plane.CreateByOriginAndBasis(target.origin, target.xDir, target.yDir);
      return _planeConverter.Convert(revitPlane);
    }

    // beyond limits? then build Speckle plane directly
    return new SOG.Plane
    {
      origin = _xyzToPointConverter.Convert(target.origin),
      xdir = _xyzToVectorConverter.Convert(target.xDir),
      ydir = _xyzToVectorConverter.Convert(target.yDir),
      normal = _xyzToVectorConverter.Convert(target.normal),
      units = _converterSettings.Current.SpeckleUnits
    };
  }
}
