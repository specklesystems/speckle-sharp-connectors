namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

public class PropertiesExtractor(
  ClassPropertiesExtractor classPropertiesExtractor,
  ParameterExtractor parameterExtractor,
  IMaterialQuantitiesToSpeckleLite materialQuantityConverter)
{
  public Dictionary<string, object?> GetProperties(DB.Element element)
  {
    // by default, always get class properties first
    Dictionary<string, object?> properties = classPropertiesExtractor.GetClassProperties(element);

    // add material quantities
    Dictionary<string, object> matQuantities = materialQuantityConverter.Convert(element);
    if (matQuantities.Count > 0)
    {
      properties.Add("Material Quantities", matQuantities);
    }

    // add parameters
    Dictionary<string, object?> parameters = parameterExtractor.GetParameters(element);
    if (parameters.Count > 0)
    {
      properties.Add("Parameters", parameters);
    }

    return properties;
  }
}
