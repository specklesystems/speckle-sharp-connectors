using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class LevelToSpeckleConverter : ITypedConverter<DB.Level, SOBR.RevitLevel>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public LevelToSpeckleConverter(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
  }

  public SOBR.RevitLevel Convert(DB.Level target)
  {
    SOBR.RevitLevel level =
      new()
      {
        elevation = _scalingService.ScaleLength(target.Elevation),
        name = target.Name,
        createView = true,
        units = _converterSettings.Current.SpeckleUnits
      };

    return level;
  }
}
