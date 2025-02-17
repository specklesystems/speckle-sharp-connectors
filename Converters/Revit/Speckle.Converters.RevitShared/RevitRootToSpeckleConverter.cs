using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.Objects.Data;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared;

public class RevitRootToSpeckleConverter : IRootToSpeckleConverter
{
  private readonly IConverterManager<IToSpeckleTopLevelConverter> _toSpeckle;
  private readonly PropertiesExtractor _propertiesExtractor;

  public RevitRootToSpeckleConverter(
    IConverterManager<IToSpeckleTopLevelConverter> toSpeckle,
    PropertiesExtractor propertiesExtractor
  )
  {
    _toSpeckle = toSpeckle;
    _propertiesExtractor = propertiesExtractor;
  }

  public Base Convert(object target)
  {
    if (target is not DB.Element element)
    {
      throw new ValidationException($"Target object is not a db element, it's a {target.GetType()}");
    }

    var objectConverter = _toSpeckle.ResolveConverter(target.GetType());

    Base result = objectConverter.Convert(target);

    // add class properties here for non-dataobjects
    // dataobject properties are already handled in their converter
    if (result is not DataObject)
    {
      var properties = _propertiesExtractor.GetProperties(element);
      if (properties.Count > 0)
      {
        result["properties"] = properties;
      }
    }

    result.applicationId = element.UniqueId;

    return result;
  }
}
