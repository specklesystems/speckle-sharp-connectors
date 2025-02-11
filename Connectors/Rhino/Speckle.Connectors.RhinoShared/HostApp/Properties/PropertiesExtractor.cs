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
      // POC: could not determine how to extract the value of a formula user string.
      // So for now we are skipping them
      if (userStrings[key].StartsWith("%<"))
      {
        continue;
      }

      properties[key] = userStrings[key];
    }

    return properties;
  }
}
