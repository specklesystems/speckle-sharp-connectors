using System.Collections.Specialized;
using Rhino.DocObjects;

namespace Speckle.Connectors.Rhino.HostApp.Properties;

/// <summary>
/// Extracts user strings out from a rhinoobject. Expects to be scoped per operation.
/// </summary>
public class UserStringsExtractor
{
  public UserStringsExtractor() { }

  /// <summary>
  /// Extracts user strings out from a rhinoobject. Expects to be scoped per operation.
  /// </summary>
  /// <param name="rhObject"></param>
  /// <returns></returns>
  public Dictionary<string, object?>? GetUserStrings(RhinoObject rhObject)
  {
    if (rhObject is null || rhObject.Attributes.UserStringCount == 0)
    {
      return null;
    }

    Dictionary<string, object?> userStringDict = new();
    NameValueCollection userStrings = rhObject.Attributes.GetUserStrings();
    foreach (var key in userStrings.AllKeys)
    {
      userStringDict.Add(key, userStrings[key]);
    }

    return userStringDict;
  }
}
