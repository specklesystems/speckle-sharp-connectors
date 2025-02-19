namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles standard property assignment without any merging or hierarchy processing.
/// </summary>
public class StandardPropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor
) : BasePropertyHandler(propertySetsExtractor, modelPropertiesExtractor)
{
  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem) => ProcessPropertySets(modelItem);
}
