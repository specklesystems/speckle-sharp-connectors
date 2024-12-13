using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class ArcToSpeckleConverter : ITypedConverter<RG.Arc, SOG.Arc>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public ArcToSpeckleConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Arc object to a Speckle Arc object.
  /// </summary>
  /// <param name="target">The Rhino Arc object to convert.</param>
  /// <returns>The converted Speckle Arc object.</returns>
  /// <remarks>
  /// This method assumes the domain of the arc is (0,1) as Arc types in Rhino do not have domain. You may want to request a conversion from ArcCurve instead.
  /// </remarks>
  public SOG.Arc Convert(RG.Arc target) =>
    new()
    {
      plane = _planeConverter.Convert(target.Plane), // POC: need to validate if this follows the Speckle arc plane handedness convention
      startPoint = _pointConverter.Convert(target.StartPoint),
      midPoint = _pointConverter.Convert(target.MidPoint),
      endPoint = _pointConverter.Convert(target.EndPoint),
      domain = SOP.Interval.UnitInterval,
      units = _settingsStore.Current.SpeckleUnits,
    };
}
