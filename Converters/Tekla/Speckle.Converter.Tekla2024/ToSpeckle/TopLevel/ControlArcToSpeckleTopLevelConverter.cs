using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ControlArc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ControlArcToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Arc, SOG.Arc> _arcConverter;

  public ControlArcToSpeckleTopLevelConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Arc, SOG.Arc> arcConverter
  )
  {
    _settingsStore = settingsStore;
    _arcConverter = arcConverter;
  }

  public Base Convert(object target) => Convert((TSM.ControlArc)target);

  public SOG.Arc Convert(TSM.ControlArc target) => _arcConverter.Convert(target.Geometry);
}
