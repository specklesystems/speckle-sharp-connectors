using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class BoxToSpeckleConverter : ITypedConverter<RG.Box, SOG.Box>
{
  private readonly ITypedConverter<RG.Plane, SOG.Plane> _planeConverter;
  private readonly ITypedConverter<RG.Interval, SOP.Interval> _intervalConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public BoxToSpeckleConverter(
    ITypedConverter<RG.Plane, SOG.Plane> planeConverter,
    ITypedConverter<RG.Interval, SOP.Interval> intervalConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _intervalConverter = intervalConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Box object to a Speckle Box object.
  /// </summary>
  /// <param name="target">The Rhino Box object to convert.</param>
  /// <returns>The converted Speckle Box object.</returns>
  public SOG.Box Convert(RG.Box target) =>
    new()
    {
      plane = _planeConverter.Convert(target.Plane),
      xSize = _intervalConverter.Convert(target.X),
      ySize = _intervalConverter.Convert(target.Y),
      zSize = _intervalConverter.Convert(target.Z),
      units = _settingsStore.Current.SpeckleUnits
    };
}
