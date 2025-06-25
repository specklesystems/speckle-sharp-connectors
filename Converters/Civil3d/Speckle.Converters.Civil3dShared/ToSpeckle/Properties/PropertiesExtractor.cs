namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts properties for the CivilObject class.
/// </summary>
public class PropertiesExtractor : Speckle.Converters.AutocadShared.ToSpeckle.IPropertiesExtractor
{
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly PartDataExtractor _partDataExtractor;
  private readonly PropertySetExtractor _propertySetExtractor;
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;

  public PropertiesExtractor(
    ClassPropertiesExtractor classPropertiesExtractor,
    PartDataExtractor partDataExtractor,
    PropertySetExtractor propertySetExtractor,
    ExtensionDictionaryExtractor extensionDictionaryExtractor
  )
  {
    _classPropertiesExtractor = classPropertiesExtractor;
    _partDataExtractor = partDataExtractor;
    _propertySetExtractor = propertySetExtractor;
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    // first get all class properties, which will be at the root level of props dictionary
    Dictionary<string, object?> properties = _classPropertiesExtractor.GetClassProperties(entity);

    // add part data, property sets, and extension dictionaries to the properties dict
    AddDictionaryToPropertyDictionary(_partDataExtractor.GetPartData(entity), "Part Data", properties);
    AddDictionaryToPropertyDictionary(_propertySetExtractor.GetPropertySets(entity), "Property Sets", properties);
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
