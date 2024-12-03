using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

public class PropertiesExtractor
{
  private readonly ParameterExtractor _parameterExtractor;
  private readonly ITypedConverter<DB.Element, Dictionary<string, object>> _materialQuantityConverter;
  private readonly ILogger<RevitRootToSpeckleConverter> _logger;

  public PropertiesExtractor(
    ParameterExtractor parameterExtractor,
    ITypedConverter<DB.Element, Dictionary<string, object>> materialQuantityConverter,
    ILogger<RevitRootToSpeckleConverter> logger
  )
  {
    _parameterExtractor = parameterExtractor;
    _materialQuantityConverter = materialQuantityConverter;
    _logger = logger;
  }

  public Dictionary<string, object?> GetProperties(DB.Element element)
  {
    Dictionary<string, object?> properties = new();

    try
    {
      Dictionary<string, object> matQuantities = _materialQuantityConverter.Convert(element);
      if (matQuantities.Count > 0)
      {
        properties.Add("Material Quantities", matQuantities);
      }
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to extract material quantities from element {element.GetType().Name}");
    }

    try
    {
      Dictionary<string, object?> parameters = _parameterExtractor.GetParameters(element);
      if (parameters.Count > 0)
      {
        properties.Add("Parameters", parameters);
      }
    }
    catch (Exception e) when (!e.IsFatal())
    {
      _logger.LogWarning(e, $"Failed to extract parameters from element {element.GetType().Name}");
    }

    return properties;
  }
}
