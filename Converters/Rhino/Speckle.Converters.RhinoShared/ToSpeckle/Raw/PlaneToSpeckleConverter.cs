using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class PlaneToSpeckleConverter : ITypedConverter<RG.Plane, SOG.Plane>
{
  private readonly ITypedConverter<RG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public PlaneToSpeckleConverter(
    ITypedConverter<RG.Vector3d, SOG.Vector> vectorConverter,
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _vectorConverter = vectorConverter;
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts an instance of Rhino Plane to Speckle Plane.
  /// </summary>
  /// <param name="target">The instance of Rhino Plane to convert.</param>
  /// <returns>The converted instance of Speckle Plane.</returns>
  public SOG.Plane Convert(RG.Plane target) =>
    new()
    {
      origin = _pointConverter.Convert(target.Origin),
      normal = _vectorConverter.Convert(target.ZAxis),
      xdir = _vectorConverter.Convert(target.XAxis),
      ydir = _vectorConverter.Convert(target.YAxis),
      units = _settingsStore.Current.SpeckleUnits,
    };
}
