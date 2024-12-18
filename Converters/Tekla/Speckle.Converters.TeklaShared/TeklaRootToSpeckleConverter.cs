using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.TeklaShared.Extensions;
using Speckle.Sdk.Common;
using Tekla.Structures.Model;

namespace Speckle.Converters.TeklaShared;

public class TeklaRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;
  private readonly ILogger<TeklaRootToSpeckleConverter> _logger;

  public TeklaRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<TeklaConversionSettings> settingsStore,
    ILogger<TeklaRootToSpeckleConverter> logger
  )
  {
    _toSpeckle = toSpeckle;
    _settingsStore = settingsStore;
    _logger = logger;
  }

  public BaseResult Convert(object target)
  {
    if (target is not ModelObject modelObject)
    {
      return BaseResult.NoConversion($"Target object is not a ModelObject. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var converterResult = _toSpeckle.ResolveConverter(type, true);
    if (converterResult.IsFailure)
    {
      return BaseResult.NoConverter(converterResult.Message);
    }

    var result = converterResult.Converter.NotNull().Convert(target);

    result.Value.applicationId = modelObject.GetSpeckleApplicationId();

    return result;
  }
}
