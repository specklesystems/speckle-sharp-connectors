using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

public class PropertiesExtractor
{
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly ParameterExtractor _parameterExtractor;
  private readonly ITypedConverter<DB.Element, Dictionary<string, object>> _materialQuantityConverter;

  public PropertiesExtractor(
    ClassPropertiesExtractor classPropertiesExtractor,
    ParameterExtractor parameterExtractor,
    ITypedConverter<DB.Element, Dictionary<string, object>> materialQuantityConverter
  )
  {
    _classPropertiesExtractor = classPropertiesExtractor;
    _parameterExtractor = parameterExtractor;
    _materialQuantityConverter = materialQuantityConverter;
  }

  public Base GetProperties(DB.Element element)
  {
    var props = new Base();
    // by default, always get class properties first
    props["@classProps"] = _classPropertiesExtractor.GetClassProperties(element);

    // add material quantities
    props["@matProps"] = _materialQuantityConverter.Convert(element);
    // if (matQuantities.Count > 0)
    // {
    //   properties.Add("Material Quantities", matQuantities);
    // }

    // add parameters
    Base parameters = _parameterExtractor.GetParameters(element);
    props["@parameters"] = parameters;

    return parameters;
  }
}
