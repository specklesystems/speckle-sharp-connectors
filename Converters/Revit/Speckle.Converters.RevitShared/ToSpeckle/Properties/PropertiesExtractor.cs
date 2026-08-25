using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.RevitShared.ToSpeckle.Properties;

public class PropertiesExtractor
{
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly ParameterExtractor _parameterExtractor;
  private readonly CompoundStructureExtractor _compoundStructureExtractor;
  private readonly ITypedConverter<DB.Element, Dictionary<string, object>> _materialQuantityConverter;

  public PropertiesExtractor(
    ClassPropertiesExtractor classPropertiesExtractor,
    ParameterExtractor parameterExtractor,
    CompoundStructureExtractor compoundStructureExtractor,
    ITypedConverter<DB.Element, Dictionary<string, object>> materialQuantityConverter
  )
  {
    _classPropertiesExtractor = classPropertiesExtractor;
    _parameterExtractor = parameterExtractor;
    _compoundStructureExtractor = compoundStructureExtractor;
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

    // add the compound element layer buildup, if this element's type has one
    if (_compoundStructureExtractor.GetCompoundStructure(element) is Dictionary<string, object?> structure)
    {
      properties.Add("Compound Structure", structure);
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
