using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.Grid), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public sealed class GridTopLevelConverterToSpeckle : IToSpeckleTopLevelConverter
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

  public Base Convert(object target) => Convert((DB.Grid)target);

  public SOBE.GridLine Convert(DB.Grid target) =>
    new()
    {
      baseLine = _curveConverter.Convert(target.Curve),
      label = target.Name,
      applicationId = target.UniqueId,
      units = _converterSettings.Current.SpeckleUnits
    };
}
