namespace Speckle.Converters.CSiShared.Utils;

/// <summary>
/// Provides utility methods for dictionary operations common across the CSI converter.
/// </summary>
public static class DictionaryUtils
{
  /// <summary>
  /// Ensures a nested dictionary exists at the specified key, creating it if necessary.
  /// Used for organizing properties into hierarchical categories (e.g., "Geometry", "Assignments", "Design").
  /// </summary>
  /// <param name="dictionary">The parent dictionary to check or modify</param>
  /// <param name="key">The key where the nested dictionary should exist</param>
  /// <returns>
  /// The existing nested dictionary if present, or a new empty dictionary after adding it to the parent
  /// </returns>
  /// <remarks>
  /// Common usage:
  /// <code>
  /// var geometry = DictionaryUtils.EnsureNestedDictionary(properties, "Geometry");
  /// geometry["startPoint"] = startPoint;
  /// geometry["endPoint"] = endPoint;
  /// </code>
  /// This pattern is used throughout property extractors to maintain consistent property organization.
  /// </remarks>
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
