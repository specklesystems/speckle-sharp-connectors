namespace Speckle.Converters.CSiShared.Utils;

/// <summary>
/// Provides extension methods for dictionary operations common across the CSI converter.
/// </summary>
public static class DictionaryExtensions
{
  /// <summary>
  /// Ensures a nested dictionary exists at the specified key, creating it if necessary.
  /// Used for organizing properties into hierarchical categories (e.g., "Geometry", "Assignments", "Design").
  /// </summary>
  /// <remarks>
  /// This pattern is used throughout property extractors to maintain consistent property organization.
  /// </remarks>
  public static Dictionary<string, object?> EnsureNested(this Dictionary<string, object?> dictionary, string key)
  {
    if (!dictionary.TryGetValue(key, out var obj) || obj is not Dictionary<string, object?> nestedDictionary)
    {
      nestedDictionary = [];
      dictionary[key] = nestedDictionary;
    }

    return nestedDictionary;
  }

  /// <summary>
  /// Adds a value with its associated units to a parent dictionary using a standardized format.
  /// Creates a nested dictionary with 'name', 'value', and 'units' keys.
  /// </summary>
  public static void AddWithUnits(
    this Dictionary<string, object?> dictionary,
    string key,
    object value,
    string? units = null
  ) =>
    dictionary[key] = new Dictionary<string, object?>
    {
      ["name"] = key,
      ["value"] = value,
      ["units"] = units
    };
}
