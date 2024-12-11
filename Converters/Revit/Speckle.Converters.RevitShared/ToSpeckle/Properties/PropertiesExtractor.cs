using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

public class PropertiesExtractor
{
  private readonly ParameterExtractor _parameterExtractor;
  private readonly ITypedConverter<DB.Element, Dictionary<string, object>> _materialQuantityConverter;

  public PropertiesExtractor(
    ParameterExtractor parameterExtractor,
    ITypedConverter<DB.Element, Dictionary<string, object>> materialQuantityConverter
  )
  {
    _parameterExtractor = parameterExtractor;
    _materialQuantityConverter = materialQuantityConverter;
  }

  public Dictionary<string, object?> GetProperties(DB.Element element)
  {
    Dictionary<string, object?> properties = new();

    Dictionary<string, object> matQuantities = _materialQuantityConverter.Convert(element);
    if (matQuantities.Count > 0)
    {
      properties.Add("Material Quantities", matQuantities);
    }

    Dictionary<string, object?> parameters = _parameterExtractor.GetParameters(element);
    if (parameters.Count > 0)
    {
      properties.Add("Parameters", parameters);
    }

    return properties;
  }
}
