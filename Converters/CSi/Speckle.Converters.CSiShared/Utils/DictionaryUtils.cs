namespace Speckle.Converters.CSiShared.Utils;

public static class DictionaryUtils
{
  public static Dictionary<string, object?> EnsureNestedDictionary(Dictionary<string, object?> dictionary, string key)
  {
    if (!dictionary.TryGetValue(key, out var obj) || obj is not Dictionary<string, object?> nestedDictionary)
    {
      nestedDictionary = new Dictionary<string, object?>();
      dictionary[key] = nestedDictionary;
    }

    return nestedDictionary;
  }
}
