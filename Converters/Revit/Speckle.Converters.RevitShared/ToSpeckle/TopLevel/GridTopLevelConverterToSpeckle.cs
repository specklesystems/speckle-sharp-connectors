using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.Grid), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public sealed class GridTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DB.Grid, SOBE.GridLine>
{
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public GridTopLevelConverterToSpeckle(
    ITypedConverter<DB.Curve, ICurve> curveConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _curveConverter = curveConverter;
    _converterSettings = converterSettings;
  }

  public override SOBE.GridLine Convert(DB.Grid target) =>
    new()
    {
      baseLine = _curveConverter.Convert(target.Curve),
      label = target.Name,
      applicationId = target.UniqueId,
      units = _converterSettings.Current.SpeckleUnits
    };
}
