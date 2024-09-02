using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.Level), 0)]
public class LevelConversionToSpeckle : BaseTopLevelConverterToSpeckle<DB.Level, SOBR.RevitLevel>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;
  private readonly ISettingsStore<RevitConversionSettings> _settings;

  public LevelConversionToSpeckle(
    ScalingServiceToSpeckle scalingService,
    ParameterObjectAssigner parameterObjectAssigner,
    ISettingsStore<RevitConversionSettings> settings
  )
  {
    _scalingService = scalingService;
    _parameterObjectAssigner = parameterObjectAssigner;
    _settings = settings;
  }

  public override SOBR.RevitLevel Convert(DB.Level target)
  {
    SOBR.RevitLevel level =
      new()
      {
        elevation = _scalingService.ScaleLength(target.Elevation),
        name = target.Name,
        createView = true,
        units = _settings.Current.SpeckleUnits
      };

    _parameterObjectAssigner.AssignParametersToBase(target, level);

    return level;
  }
}
