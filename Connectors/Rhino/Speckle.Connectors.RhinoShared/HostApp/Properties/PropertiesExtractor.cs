using Rhino;
using Rhino.Collections;
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
    Dictionary<string, object?> properties = GetUserStrings(rhObject);

    Dictionary<string, object?> userDict = GetUserDict(rhObject);
    if (userDict.Count > 0)
    {
      properties["User Dictionary"] = userDict;
    }

    return properties;
  }

  private Dictionary<string, object?> GetUserDict(RhinoObject rhObject)
  {
    Dictionary<string, object?> userDict = new();
    if (rhObject.UserDictionary != null && rhObject.UserDictionary.Count > 0)
    {
      ParseArchivableToDictionary(userDict, rhObject.UserDictionary);
    }

    return userDict;
  }

  /// <summary>
  /// Copies an ArchivableDictionary to a Dictionary
  /// </summary>
  /// <param name="target"></param>
  /// <param name="dict"></param>
  private void ParseArchivableToDictionary(Dictionary<string, object?> target, ArchivableDictionary dict)
  {
    foreach (var key in dict.Keys)
    {
      var obj = dict[key];
      switch (obj)
      {
        case ArchivableDictionary o:
          Dictionary<string, object?> nested = new();
          ParseArchivableToDictionary(nested, o);
          target[key] = nested;
          continue;

        case double:
        case bool:
        case int:
        case string:
        case IEnumerable<double>:
        case IEnumerable<bool>:
        case IEnumerable<int>:
        case IEnumerable<string>:
          target[key] = obj;
          continue;

        default:
          continue;
      }
    }
  }

  private Dictionary<string, object?> GetUserStrings(RhinoObject rhObject)
  {
    Dictionary<string, object?> userStringDict = new();
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
          userStringDict[key] = value;
          continue;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // Shh. We can fail silently here - it's not even worth logging. I expect users will complain properties are missing.
      }

      userStringDict[key] = userStrings[key];
    }

    // NOTE: if no mapping was found on the object, check layer(s) recursively
    if (!userStringDict.ContainsKey(RevitMappingConstants.CATEGORY_USER_STRING_KEY))
    {
      var layerMapping = _revitMappingResolver.SearchLayerHierarchyForMapping(rhObject);
      if (!string.IsNullOrEmpty(layerMapping))
      {
        userStringDict[RevitMappingConstants.CATEGORY_USER_STRING_KEY] = layerMapping;
      }
    }

    return userStringDict;
  }
}
