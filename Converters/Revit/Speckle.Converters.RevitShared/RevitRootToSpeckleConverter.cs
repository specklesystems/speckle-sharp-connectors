using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

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

  public Base Convert(object target)
  {
    if (target is not DB.Element element)
    {
      throw new ValidationException($"Target object is not a db element, it's a {target.GetType()}");
    }

    var objectConverter = _toSpeckle.ResolveConverter(target.GetType());

    Base result = objectConverter.Convert(target);

    result.applicationId = element.UniqueId;

    // Add ElementID to the converted objects
    result["elementId"] = element.Id.ToString()!;

    // POC DirectShapes have RevitCategory enum as the type or the category property, DS category property is already set in the converter
    if (target is not DB.DirectShape)
    {
      result["builtinCategory"] = element.Category?.GetBuiltInCategory().ToString();
    }

    result["worksetId"] = element.WorksetId.ToString();
    if (!_worksetCache.TryGetValue(element.WorksetId, out var worksetName))
    {
      DB.Workset workset = _converterSettings.Current.Document.GetWorksetTable().GetWorkset(element.WorksetId);
      worksetName = workset.Name;
      _worksetCache[element.WorksetId] = worksetName;
    }
    result["worksetName"] = worksetName;

    return result;
  }
}
