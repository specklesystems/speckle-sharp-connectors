using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.RevitShared;

public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  private readonly Dictionary<DB.WorksetId, string> _worksetCache = new();

  public RevitRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _toSpeckle = toSpeckle;
    _converterSettings = converterSettings;
  }

  public BaseResult Convert(object target)
  {
    if (target is not DB.Element element)
    {
      throw new ValidationException($"Target object is not a db element, it's a {target.GetType()}");
    }

    var resolveResult = _toSpeckle.ResolveConverter(target.GetType());
    if (resolveResult.IsFailure)
    {
      return BaseResult.NoConverter(resolveResult.Message);
    }

    var converterResult = resolveResult.Converter.NotNull().Convert(target);
    if (converterResult.IsFailure)
    {
      return BaseResult.NoConversion(converterResult.Message);
    }

    var result = converterResult.Base.NotNull();
    result.applicationId = element.UniqueId;

    // Add ElementID to the converted objects
    result["elementId"] = element.Id.ToString()!;

    result["builtInCategory"] = element.Category?.GetBuiltInCategory().ToString();

    result["worksetId"] = element.WorksetId.ToString();
    if (!_worksetCache.TryGetValue(element.WorksetId, out var worksetName))
    {
      DB.Workset workset = _converterSettings.Current.Document.GetWorksetTable().GetWorkset(element.WorksetId);
      worksetName = workset.Name;
      _worksetCache[element.WorksetId] = worksetName;
    }
    result["worksetName"] = worksetName;

    return  BaseResult.Success(result);
  }
}
