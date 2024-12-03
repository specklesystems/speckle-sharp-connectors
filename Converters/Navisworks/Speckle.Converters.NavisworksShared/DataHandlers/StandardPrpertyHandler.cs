namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles standard property assignment without any merging or hierarchy processing.
/// </summary>
public class StandardPropertyHandler(
  ClassPropertiesExtractor classPropertiesExtractor,
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor
) : BasePropertyHandler(classPropertiesExtractor, propertySetsExtractor, modelPropertiesExtractor)
{
  protected override void AssignPropertySets(SSM.Base speckleObject, NAV.ModelItem modelItem)
  {
    if (speckleObject == null)
    {
      throw new ArgumentNullException(nameof(speckleObject));
    }
    var propertyDictionary = speckleObject["properties"] as Dictionary<string, object?> ?? [];

    var categoryDictionaries = ProcessPropertySets(modelItem);

    foreach (var kvp in categoryDictionaries)
    {
      categoryDictionaries[$"{kvp.Key}"] = kvp.Value;
    }

    speckleObject["properties"] = propertyDictionary;
  }
}
