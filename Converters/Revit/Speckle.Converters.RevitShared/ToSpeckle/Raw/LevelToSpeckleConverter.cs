using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class LevelToSpeckleConverter : ITypedConverter<DB.Level, Dictionary<string, object>>
{
  private readonly ScalingServiceToSpeckle _scalingService;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  private Dictionary<DB.ElementId, Dictionary<string, object>> _cache = new();

  public LevelToSpeckleConverter(
    ScalingServiceToSpeckle scalingService,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _scalingService = scalingService;
    _converterSettings = converterSettings;
  }

  public Dictionary<string, object> Convert(DB.Level target)
  {
    if (!_cache.TryGetValue(target.Id, out Dictionary<string, object>? level))
    {
      level = new()
      {
        ["elevation"] = _scalingService.ScaleLength(target.Elevation),
        ["name"] = target.Name,
        ["units"] = _converterSettings.Current.SpeckleUnits
      };
      _cache[target.Id] = level;
    }

    return level;
  }
}
