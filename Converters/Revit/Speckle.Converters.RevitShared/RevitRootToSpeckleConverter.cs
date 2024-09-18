using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterResolver<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ITypedConverter<DB.Element, List<Dictionary<string, object>>> _materialQuantityConverter;
  private readonly ParameterExtractor _parameterExtractor;
  private readonly ILogger<RevitRootToSpeckleConverter> _logger;

  public RevitRootToSpeckleConverter(
    IConverterResolver<IToSpeckleTopLevelConverter> toSpeckle,
    ITypedConverter<DB.Element, List<Dictionary<string, object>>> materialQuantityConverter,
    ParameterExtractor parameterExtractor,
    ILogger<RevitRootToSpeckleConverter> logger
  )
  {
    _toSpeckle = toSpeckle;
    _materialQuantityConverter = materialQuantityConverter;
    _parameterExtractor = parameterExtractor;
    _logger = logger;
  }

  public Base Convert(object target)
  {
    if (target is not DB.Element element)
    {
      throw new SpeckleConversionException($"Target object is not a db element, it's a {target.GetType()}");
    }

    var objectConverter = _toSpeckle.GetConversionForType(target.GetType());

    if (objectConverter == null)
    {
      throw new SpeckleConversionException($"No conversion found for {target.GetType().Name}");
    }

    Base result =
      objectConverter.Convert(target)
      ?? throw new SpeckleConversionException($"Conversion of object with type {target.GetType()} returned null");

    result.applicationId = element.UniqueId;

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
      result["revitParams"] = parameters;
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to extract parameters from element {target.GetType().Name}");
    }

    return result;
  }
}
