namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>
/// Extracts properties for autocad entities. NOTE: currently not in use in acad
/// </summary>
public class PropertiesExtractor : IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;
  private readonly XDataExtractor _xDataExtractor;

  public PropertiesExtractor(ExtensionDictionaryExtractor extensionDictionaryExtractor, XDataExtractor xDataExtractor)
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
    _xDataExtractor = xDataExtractor;
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    Dictionary<string, object?> properties = new();
    AddDictionaryToPropertyDictionary(
      _extensionDictionaryExtractor.GetExtensionDictionary(entity),
      "Extension Dictionary",
      properties
    );
    AddDictionaryToPropertyDictionary(_xDataExtractor.GetXData(entity), "XData", properties);

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
