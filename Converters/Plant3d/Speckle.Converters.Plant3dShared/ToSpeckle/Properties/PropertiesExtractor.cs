using Speckle.Converters.Common;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts properties for Plant3D objects.
/// </summary>
public class PropertiesExtractor : Speckle.Converters.AutocadShared.ToSpeckle.IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;
  private readonly string _drawingName;

  public PropertiesExtractor(
    ExtensionDictionaryExtractor extensionDictionaryExtractor,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore
  )
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
    _drawingName = Path.GetFileName(settingsStore.Current.Document.Name);
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    Dictionary<string, object?> properties = new();

    // Add source drawing name so objects from multi-drawing Plant 3D
    // projects can be traced back to their origin file.
    properties["Drawing Name"] = _drawingName;

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
