using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

// POC: maybe possible to restrict the access so this cannot be created directly?
public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterResolver<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly IRevitConversionContextStack _contextStack;

  public RevitRootToSpeckleConverter(
    IConverterResolver<IToSpeckleTopLevelConverter> toSpeckle,
    ParameterValueExtractor parameterValueExtractor,
    IRevitConversionContextStack contextStack
  )
  {
    _toSpeckle = toSpeckle;
    _parameterValueExtractor = parameterValueExtractor;
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
      _parameterValueExtractor.RemoveUniqueId(element.UniqueId);
    }

    return result;
  }
}
