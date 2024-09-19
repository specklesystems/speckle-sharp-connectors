using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

public class LineToSpeckleConverter : ITypedConverter<RG.Line, SOG.Line>, ITypedConverter<RG.LineCurve, SOG.Line>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public LineToSpeckleConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Line object to a Speckle Line object.
  /// </summary>
  /// <param name="target">The Rhino Line object to convert.</param>
  /// <returns>The converted Speckle Line object.</returns>
  /// <remarks>
  /// ⚠️ This conversion assumes the domain of a line is (0, LENGTH), as Rhino Lines do not have domain. If you want the domain preserved use LineCurve conversions instead.
  /// </remarks>
  public SOG.Line Convert(RG.Line target) =>
    new()
    {
      start = _pointConverter.Convert(target.From),
      end = _pointConverter.Convert(target.To),
      units = _settingsStore.Current.SpeckleUnits,
      domain = new SOP.Interval { start = 0, end = target.Length },
      bbox = _boxConverter.Convert(new RG.Box(target.BoundingBox))
    };

  public SOG.Line Convert(RG.LineCurve target) => Convert(target.Line);
}
