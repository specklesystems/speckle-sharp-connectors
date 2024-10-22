using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Tekla.Structures.Model;

namespace Speckle.Converter.Tekla2024;

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

  public Base Convert(object target)
  {
    if (target is not ModelObject modelObject)
    {
      throw new ValidationException($"Target object is not a ModelObject. It's a ${target.GetType()}");
    }

    Type type = target.GetType();
    var objectConverter = _toSpeckle.ResolveConverter(type, true);

    Base result = objectConverter.Convert(target);

    // add tekla specific identifiers
    result.applicationId = modelObject.Identifier.GUID.ToString();
    result["modelObjectID"] = modelObject.Identifier.ID.ToString();

    //TODO: attach properties

    return result;
  }
}
