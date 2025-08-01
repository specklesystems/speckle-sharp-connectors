using Rhino;
using Rhino.DocObjects;
using Speckle.Sdk;

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
    foreach (string? key in userStrings.AllKeys)
    {
      if (key == null)
      {
        continue;
      }

      try
      {
        if (key == "$block-instance-original-object-id$") // skip: this seems to be an invisible user string that shows up on block instances
        {
          continue;
        }

        if (userStrings[key]?.StartsWith("%<") ?? false)
        {
          var value = RhinoApp.ParseTextField(userStrings[key], rhObject, null);
          properties[key] = value;
          continue;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // Shh. We can fail silently here - it's not even worth logging. I expect users will complain properties are missing.
      }

      properties[key] = userStrings[key];
    }

    return properties;
  }
}
