using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converter.Tekla2024.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(TSM.ControlCircle), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ControlPolycurveToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ITypedConverter<TG.Polycurve, SOG.Polycurve> _polycurveConverter;

  public ControlPolycurveToSpeckleTopLevelConverter(
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ITypedConverter<TG.Polycurve, SOG.Polycurve> polycurveConverter
  )
  {
    _settingsStore = settingsStore;
    _polycurveConverter = polycurveConverter;
  }

  public Base Convert(object target) => Convert((TSM.ControlPolycurve)target);

  public SOG.Polycurve Convert(TSM.ControlPolycurve target) => _polycurveConverter.Convert(target.Geometry);
}
