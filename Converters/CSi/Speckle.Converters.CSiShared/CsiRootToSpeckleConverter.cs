using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common;

namespace Speckle.Converters.CSiShared;

public class CsiRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ILogger<CsiRootToSpeckleConverter> _logger;

  public CsiRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ILogger<CsiRootToSpeckleConverter> logger
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _logger = logger;
  }

  public BaseResult Convert(object target)
  {
    if (target is not ICsiWrapper)
    {
      return BaseResult.NoConversion($"Target object is not a CSiWrapper. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var converterResult = _toSpeckle.ResolveConverter(type, true);
    if (converterResult.IsFailure)
    {
      return BaseResult.NoConverter(converterResult.Message);
    }

    var result = converterResult.Converter.NotNull().Convert(target);

    return result;
  }
}
