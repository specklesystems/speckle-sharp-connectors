namespace Speckle.Converters.Civil3dShared.ToSpeckle;

/// <summary>
/// Extracts properties for the CivilObject class.
/// </summary>
public class PropertiesExtractor
{
  private readonly GeneralPropertiesExtractor _generalPropertiesExtractor;
  private readonly PartDataExtractor _partDataExtractor;
  private readonly PropertySetExtractor _propertySetExtractor;
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;

  public PropertiesExtractor(
    GeneralPropertiesExtractor generalPropertiesExtractor,
    PartDataExtractor partDataExtractor,
    PropertySetExtractor propertySetExtractor,
    ExtensionDictionaryExtractor extensionDictionaryExtractor
  )
  {
    _generalPropertiesExtractor = generalPropertiesExtractor;
    _partDataExtractor = partDataExtractor;
    _propertySetExtractor = propertySetExtractor;
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
  }

  public Dictionary<string, object?> GetProperties(CDB.Entity entity)
  {
    Dictionary<string, object?> properties = new();

    AddDictionaryToPropertyDictionary(
      _generalPropertiesExtractor.GetGeneralProperties(entity),
      "General Properties",
      properties
    );
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
