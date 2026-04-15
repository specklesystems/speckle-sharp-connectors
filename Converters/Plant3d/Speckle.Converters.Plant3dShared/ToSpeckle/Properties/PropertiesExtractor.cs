using Speckle.Converters.Common;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts properties for Plant3D objects.
/// </summary>
public class PropertiesExtractor : Speckle.Converters.AutocadShared.ToSpeckle.IPropertiesExtractor
{
  private readonly ExtensionDictionaryExtractor _extensionDictionaryExtractor;
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;

  public PropertiesExtractor(
    ExtensionDictionaryExtractor extensionDictionaryExtractor,
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore
  )
  {
    _extensionDictionaryExtractor = extensionDictionaryExtractor;
    _settingsStore = settingsStore;
  }

  public Dictionary<string, object?> GetProperties(ADB.Entity entity)
  {
    Dictionary<string, object?> properties = new();

    // Add source drawing name so objects from multi-drawing Plant 3D
    // projects can be traced back to their origin file.
    properties["Drawing Name"] = Path.GetFileName(_settingsStore.Current.Document.Name);

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
