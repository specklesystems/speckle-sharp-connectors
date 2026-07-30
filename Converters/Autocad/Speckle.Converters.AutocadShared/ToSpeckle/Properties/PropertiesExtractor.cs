namespace Speckle.Converters.AutocadShared.ToSpeckle;

/// <summary>
/// Extracts properties for autocad entities. NOTE: currently not in use in acad
/// </summary>
public class PropertiesExtractor : IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;
  private readonly XDataExtractor _xDataExtractor;
  private readonly TextPropertiesExtractor _textPropertiesExtractor;

  public PropertiesExtractor(
    ExtensionDictionaryExtractor extensionDictionaryExtractor,
    XDataExtractor xDataExtractor,
    TextPropertiesExtractor textPropertiesExtractor
  )
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
    _xDataExtractor = xDataExtractor;
    _textPropertiesExtractor = textPropertiesExtractor;
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
    AddDictionaryToPropertyDictionary(_textPropertiesExtractor.GetTextProperties(entity), "Text", properties);

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
