using Speckle.Converters.Common.Objects;

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

  public Dictionary<string, object?> GetProperties(DB.Element element)
  {
    // by default, always get class properties first
    Dictionary<string, object?> properties = _classPropertiesExtractor.GetClassProperties(element);

    // add material quantities
    Dictionary<string, object> matQuantities = _materialQuantityConverter.Convert(element);
    if (matQuantities.Count > 0)
    {
      properties.Add("Material Quantities", matQuantities);
    }

    // add parameters
    Dictionary<string, object?> parameters = _parameterExtractor.GetParameters(element);
    if (parameters.Count > 0)
    {
      properties.Add("Parameters", parameters);
    }

    return properties;
  }
}
