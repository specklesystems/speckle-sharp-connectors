using Rhino.DocObjects;
using Speckle.Sdk.Models;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.Rhino.Extensions;

public static class SpeckleAttributeExtensions
{
  private const string PROPERTY_PATH_DELIMITER = ".";

  /// <summary>
  /// Gets Rhino object attributes from a Base, including name and user strings.
  /// </summary>
  /// <param name="base"></param>
  /// <returns></returns>
  public static ObjectAttributes GetAttributes(this Base @base)
  {
    string name = @base["name"] as string ?? "";
    ObjectAttributes atts = new() { Name = name };
    Dictionary<string, string> userStrings = new();
    Dictionary<string, object?> properties = @base is DataObject dataObj
      ? dataObj.properties ?? []
      : @base["properties"] as Dictionary<string, object?> ?? [];
    FlattenDictionaryToUserStrings(properties, userStrings, "");
    foreach (var kvp in userStrings)
    {
      // POC: we're skipping properties that end with `.name` , `.units`, etc because this is causing a lot of noise atm.
      if (kvp.Key.EndsWith(".units") || kvp.Key.EndsWith(".name") || kvp.Key.EndsWith(".internalDefinitionName"))
      {
        continue;
      }

      atts.SetUserString(kvp.Key, kvp.Value);
    }

    return atts;
  }

  // changes a properties dictionary to <string,string> to assign as user strings.
  private static void FlattenDictionaryToUserStrings(
    Dictionary<string, object?> dict,
    Dictionary<string, string> flattenedDict,
    string keyPrefix
  )
  {
    foreach (var kvp in dict)
    {
      string newKey = string.IsNullOrEmpty(keyPrefix) ? kvp.Key : $"{keyPrefix}{PROPERTY_PATH_DELIMITER}{kvp.Key}";

      if (kvp.Value is Dictionary<string, object?> childDict)
      {
        FlattenDictionaryToUserStrings(childDict, flattenedDict, newKey);
      }
      else
      {
        flattenedDict.Add(newKey, kvp.Value?.ToString() ?? "");
      }
    }
  }
}
