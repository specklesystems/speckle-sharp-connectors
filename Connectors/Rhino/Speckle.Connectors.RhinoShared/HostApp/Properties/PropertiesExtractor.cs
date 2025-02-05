using Rhino.DocObjects;

namespace Speckle.Connectors.Rhino.HostApp.Properties;

/// <summary>
/// Extracts properties for rhino objects.
/// </summary>
public class PropertiesExtractor
{
  public Dictionary<string, object?> GetProperties(RhinoObject rhObject)
  {
    Dictionary<string, object?> properties = new();
    var userStrings = rhObject.Attributes.GetUserStrings();
    foreach (var key in userStrings.AllKeys)
    {
      if (userStrings[key].StartsWith("%<"))
      {
        continue;
      }
      properties[key] = userStrings[key];
    }

    return properties;
  }
}
