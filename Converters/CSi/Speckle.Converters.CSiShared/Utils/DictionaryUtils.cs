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
  /// <remarks>
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
