using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

// POC: maybe possible to restrict the access so this cannot be created directly?
public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ITypedConverter<DB.Element, List<Dictionary<string, object>>> _materialQuantityConverter;

  public RevitRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    ITypedConverter<DB.Element, List<Dictionary<string, object>>> materialQuantityConverter
  )
  {
    _toSpeckle = toSpeckle;
    _materialQuantityConverter = materialQuantityConverter;
  }

  // POC: our assumption here is target is valid for conversion
  // if it cannot be converted then we should throw
  public Base Convert(object target)
  {
    var objectConverter = _toSpeckle.ResolveConverter(target.GetType().Name);

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

      // POC: we've discussed sending Materials as Proxies, containing the object ids of material quantities.
      // POC: this would require redesigning the MaterialQuantities class to no longer have Material as a property. TBD post december.
      // POC: should we assign parameters here instead?
      //_parameterObjectAssigner.AssignParametersToBase(element, result);
      // _parameterValueExtractor.RemoveUniqueId(element.UniqueId);
    }

    return result;
  }
}
