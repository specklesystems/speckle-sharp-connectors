using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ITypedConverter<DB.Element, List<Dictionary<string, object>>> _materialQuantityConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ParameterExtractor _parameterExtractor;
  private readonly ILogger<RevitRootToSpeckleConverter> _logger;

  private readonly Dictionary<WorksetId, string> _worksetCache = new();

  public RevitRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    ITypedConverter<DB.Element, List<Dictionary<string, object>>> materialQuantityConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ParameterExtractor parameterExtractor,
    ILogger<RevitRootToSpeckleConverter> logger
  )
  {
    _toSpeckle = toSpeckle;
    _materialQuantityConverter = materialQuantityConverter;
    _converterSettings = converterSettings;
    _parameterExtractor = parameterExtractor;
    _logger = logger;
  }

  public Base Convert(object target)
  {
    if (target is not DB.Element element)
    {
      throw new SpeckleConversionException($"Target object is not a db element, it's a {target.GetType()}");
    }

    var objectConverter = _toSpeckle.ResolveConverter(target.GetType(), true);

    if (objectConverter == null)
    {
      throw new SpeckleConversionException($"No conversion found for {target.GetType().Name}");
    }

    Base result =
      objectConverter.Convert(target)
      ?? throw new SpeckleConversionException($"Conversion of object with type {target.GetType()} returned null");

    result.applicationId = element.UniqueId;

    // Add ElementID to the converted objects
    result["elementId"] = element.Id.ToString()!;

    // POC DirectShapes have RevitCategory enum as the type or the category property, DS category property is already set in the converter
    // trying to set the category as a string will throw
    // the category should be moved to be set in each converter instead of the root to speckle converter
    if (target is not DB.DirectShape)
    {
      result["category"] = element.Category?.Name;
    }

    try
    {
      result["materialQuantities"] = _materialQuantityConverter.Convert(element);
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to extract material quantities from element {target.GetType().Name}");
    }

    try
    {
      var parameters = _parameterExtractor.GetParameters(element);
      // NOTE: we're conflicting with a strongly typed (Base) `parameters` property set on revit elements. We can revert this to be back to parameters later, but this will mean frontend legwork to add another special parsing case for the properties view of an object.
      result["properties"] = parameters;
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to extract parameters from element {target.GetType().Name}");
    }

    result["worksetId"] = element.WorksetId.ToString();
    if (!_worksetCache.TryGetValue(element.WorksetId, out var worksetName))
    {
      Workset workset = _converterSettings.Current.Document.GetWorksetTable().GetWorkset(element.WorksetId);
      worksetName = workset.Name;
      _worksetCache[element.WorksetId] = worksetName;
    }
    result["worksetName"] = worksetName;

    return result;
  }
}
