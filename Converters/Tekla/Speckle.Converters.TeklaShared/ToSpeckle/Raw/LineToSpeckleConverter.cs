using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Raw;

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
    _pointConverter = pointConverter;
  }

  public SOG.Line Convert(TG.LineSegment target) =>
    new()
    {
      start = _pointConverter.Convert(target.StartPoint),
      end = _pointConverter.Convert(target.EndPoint),
      units = _settingsStore.Current.SpeckleUnits,
    };
}
