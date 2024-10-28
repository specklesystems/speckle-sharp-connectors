using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using SOG = Speckle.Objects.Geometry;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Raw;

public class TeklaLineConverter : ITypedConverter<TG.LineSegment, SOG.Line>
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Point, SOG.Point> _pointConverter;

  public TeklaLineConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Point, SOG.Point> pointConverter
  )
  {
    _settingsStore = settingsStore;
    this._pointConverter = pointConverter;
  }

  public SOG.Line Convert(TG.LineSegment target) =>
    new()
    {
      start = _pointConverter.Convert(target.StartPoint),
      end = _pointConverter.Convert(target.EndPoint),
      units = _settingsStore.Current.SpeckleUnits
    };
}
