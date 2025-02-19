namespace Speckle.Converter.Navisworks.ToSpeckle.PropertyHandlers;

/// <summary>
/// Handles standard property assignment without any merging or hierarchy processing.
/// </summary>
public class StandardPropertyHandler(
  PropertySetsExtractor propertySetsExtractor,
  ModelPropertiesExtractor modelPropertiesExtractor
) : BasePropertyHandler(propertySetsExtractor, modelPropertiesExtractor)
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

  public override Dictionary<string, object?> GetProperties(NAV.ModelItem modelItem)
  {
    Dictionary<string, object?> categoryDictionaries = ProcessPropertySets(modelItem);

    foreach (var kvp in categoryDictionaries)
    {
      categoryDictionaries[$"{kvp.Key}"] = kvp.Value;
    }

    var classProperties = ClassPropertiesExtractor.GetClassProperties(modelItem);

    // for each of the the keys of classProperties, add them to the propertyDict
    if (classProperties == null)
    {
      return categoryDictionaries;
    }

    foreach (var kvp in classProperties)
    {
      if (categoryDictionaries.ContainsKey(kvp.Key))
      {
        categoryDictionaries[kvp.Key] = kvp.Value;
      }
      else
      {
        categoryDictionaries.Add(kvp.Key, kvp.Value);
      }
    }

    return categoryDictionaries;
  }
}
