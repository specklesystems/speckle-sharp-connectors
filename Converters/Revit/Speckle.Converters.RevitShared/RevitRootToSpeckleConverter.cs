using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using MaterialQuantity = Speckle.Objects.Other.MaterialQuantity;

namespace Speckle.Converters.RevitShared;

// POC: maybe possible to restrict the access so this cannot be created directly?
public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterResolver<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly ITypedConverter<DB.Element, IEnumerable<MaterialQuantity>> _materialQuantityConverter;
  private readonly IRevitConversionContextStack _contextStack;

  public RevitRootToSpeckleConverter(
    IConverterResolver<IToSpeckleTopLevelConverter> toSpeckle,
    ParameterValueExtractor parameterValueExtractor,
    ITypedConverter<DB.Element, IEnumerable<MaterialQuantity>> materialQuantityConverter,
    IRevitConversionContextStack contextStack
  )
  {
    _toSpeckle = toSpeckle;
    _parameterValueExtractor = parameterValueExtractor;
    _materialQuantityConverter = materialQuantityConverter;
    _contextStack = contextStack;
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

    // POC : where should logic common to most objects go?
    // shouldn't target ALWAYS be DB.Element?
    // Dim thinks so, FWIW
    if (target is DB.Element element)
    {
      // POC: is this the right place?
      result.applicationId = element.UniqueId;

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
      _parameterValueExtractor.RemoveUniqueId(element.UniqueId);
    }

    return result;
  }
}
