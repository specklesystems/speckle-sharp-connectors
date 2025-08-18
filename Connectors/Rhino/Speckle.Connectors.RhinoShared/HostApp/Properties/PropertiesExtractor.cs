using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Rhino.Mapper.Revit;
using Speckle.Sdk;

namespace Speckle.Connectors.Rhino.HostApp.Properties;

/// <summary>
/// Extracts properties for rhino objects.
/// </summary>
public class PropertiesExtractor
{
  private readonly RevitMappingResolver _revitMappingResolver;

  public PropertiesExtractor(RevitMappingResolver revitMappingResolver)
  {
    _revitMappingResolver = revitMappingResolver;
  }

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

    // NOTE: if no mapping was found on the object, check layer(s) recursively
    if (!properties.ContainsKey(RevitMappingConstants.CATEGORY_USER_STRING_KEY))
    {
      var layerMapping = _revitMappingResolver.SearchLayerHierarchyForMapping(rhObject);
      if (!string.IsNullOrEmpty(layerMapping))
      {
        properties[RevitMappingConstants.CATEGORY_USER_STRING_KEY] = layerMapping;
      }
    }

    return properties;
  }
}
