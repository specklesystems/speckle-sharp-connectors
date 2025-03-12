using System.Collections;

namespace Speckle.Converters.TeklaShared.ToSpeckle.Helpers;

public class UserDefinedAttributesExtractor
{
  public Dictionary<string, object?> GetUserDefinedAttributes(TSM.ModelObject modelObject)
  {
    var userProperties = new Dictionary<string, object?>();
    var userValues = new Hashtable();

    if (!modelObject.GetAllUserProperties(ref userValues))
    {
      // NOTE: Return empty dictionary if no user properties found or failed to get properties
      return userProperties;
    }

    foreach (DictionaryEntry entry in userValues)
    {
      if (entry.Value != null && !string.IsNullOrEmpty(entry.Value.ToString()))
      {
        var propertyName = entry.Key.ToString();
        var propertyValue = entry.Value;

        if (propertyName != null)
        {
          userProperties[propertyName] = propertyValue;
        }
      }
    }

    return userProperties;
  }
}
