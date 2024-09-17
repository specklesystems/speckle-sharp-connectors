using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class LineSegment3dToSpeckleRawConverter : ITypedConverter<AG.LineSegment3d, SOG.Line>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public LineSegment3dToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Line Convert(AG.LineSegment3d target) =>
    new()
    {
      start = _pointConverter.Convert(target.StartPoint),
      end = _pointConverter.Convert(target.EndPoint),
      units = _settingsStore.Current.SpeckleUnits,
      domain = new SOP.Interval { start = 0, end = target.Length },
    };
}
