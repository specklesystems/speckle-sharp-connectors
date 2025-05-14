using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class CircleToSpeckleConverter : ITypedConverter<RG.Circle, SOG.Circle>
{
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public CircleToSpeckleConverter(
    ITypedConverter<RG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a RG.Circle object to a SOG.Circle object.
  /// </summary>
  /// <param name="target">The RG.Circle object to convert.</param>
  /// <returns>The converted SOG.Circle object.</returns>
  /// <remarks>
  /// ⚠️ This conversion assumes the domain of a circle is (0,1) as Rhino Circle types do not have a domain. If you want to preserve the domain use ArcCurve conversion instead.
  /// </remarks>
  public SOG.Circle Convert(RG.Circle target) =>
    new()
    {
      plane = _planeConverter.Convert(target.Plane),
      radius = target.Radius,
      units = _settingsStore.Current.SpeckleUnits,
      domain = SOP.Interval.UnitInterval
    };
}
