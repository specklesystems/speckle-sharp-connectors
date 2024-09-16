using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Revit2023.ToSpeckle.Parameters;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

// POC: maybe possible to restrict the access so this cannot be created directly?
public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterResolver<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ITypedConverter<DB.Element, List<Dictionary<string, object>>> _materialQuantityConverter;
  private readonly ParameterExtractor _parameterExtractor;

  public RevitRootToSpeckleConverter(
    IConverterResolver<IToSpeckleTopLevelConverter> toSpeckle,
    ITypedConverter<DB.Element, List<Dictionary<string, object>>> materialQuantityConverter,
    ParameterExtractor parameterExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _materialQuantityConverter = materialQuantityConverter;
    _parameterExtractor = parameterExtractor;
  }

  // POC: our assumption here is target is valid for conversion
  // if it cannot be converted then we should throw
  public Base Convert(object target)
  {
    var objectConverter = _toSpeckle.GetConversionForType(target.GetType());

    if (objectConverter == null)
    {
      throw new SpeckleConversionException($"No conversion found for {target.GetType().Name}");
    }

    Base result =
      objectConverter.Convert(target)
      ?? throw new SpeckleConversionException($"Conversion of object with type {target.GetType()} returned null");

    if (target is DB.Element element) // Note: aren't all targets DB elements?
    {
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
        // TODO: report quantities not retrievable
      }

      var _ = _parameterExtractor.GetParameters(element);
    }

    return result;
  }
}
