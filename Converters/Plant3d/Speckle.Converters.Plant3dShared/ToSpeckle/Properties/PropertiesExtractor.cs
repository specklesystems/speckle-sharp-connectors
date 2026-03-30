namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts properties for Plant3D objects.
/// </summary>
public class PropertiesExtractor : Speckle.Converters.AutocadShared.ToSpeckle.IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;

  public PropertiesExtractor(ExtensionDictionaryExtractor extensionDictionaryExtractor)
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    Dictionary<string, object?> properties = new();

    // TODO: Add Plant3D class-specific property extraction here
    // For example, extract pipe spec data, equipment data, etc.

    // add property sets and extension dictionaries to the properties dict
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
