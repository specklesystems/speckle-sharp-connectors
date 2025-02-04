using Rhino.DocObjects;

namespace Speckle.Connectors.Rhino.HostApp.Properties;

/// <summary>
/// Extracts properties for rhino objects.
/// </summary>
public class PropertiesExtractor
{
  private readonly UserStringsExtractor _userStringsExtractor;

  public PropertiesExtractor(UserStringsExtractor userStringsExtractor)
  {
    _userStringsExtractor = userStringsExtractor;
  }

  public Dictionary<string, object?> GetProperties(RhinoObject rhObject)
  {
    Dictionary<string, object?> properties = new();
    AddDictionaryToPropertyDictionary(_userStringsExtractor.GetUserStrings(rhObject), "User Strings", properties);

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
