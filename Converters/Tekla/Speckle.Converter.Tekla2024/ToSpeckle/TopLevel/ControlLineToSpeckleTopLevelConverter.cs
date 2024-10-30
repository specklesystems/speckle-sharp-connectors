using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ControlLine), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ControlLineToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.LineSegment, SOG.Line> _lineConverter;

  public ControlLineToSpeckleTopLevelConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.LineSegment, SOG.Line> lineConverter
  )
  {
    _settingsStore = settingsStore;
    _lineConverter = lineConverter;
  }

  public Base Convert(object target) => Convert((TSM.ControlLine)target);

  public SOG.Line Convert(TSM.ControlLine target) => _lineConverter.Convert(target.Line);
}
