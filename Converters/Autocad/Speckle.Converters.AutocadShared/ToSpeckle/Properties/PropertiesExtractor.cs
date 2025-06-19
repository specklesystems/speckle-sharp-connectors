namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>
/// Extracts properties for autocad entities. NOTE: currently not in use in acad
/// </summary>
public class PropertiesExtractor : IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;

  public PropertiesExtractor(ExtensionDictionaryExtractor extensionDictionaryExtractor)
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    Dictionary<string, object?> properties = new();
    AddDictionaryToPropertyDictionary(
      _extensionDictionaryExtractor.GetExtensionDictionary(entity),
      "Extension Dictionary",
      properties
    );

    return properties;
  }

  private void AddDictionaryToPropertyDictionary(
    Dictionary<string, object?>? entryDictionary,
    string entryName,
    Dictionary<string, object?> propertyDictionary
  )
  {
    if (entryDictionary is not null && entryDictionary.Count > 0)
    {
      propertyDictionary.Add(entryName, entryDictionary);
    }
  }
}
