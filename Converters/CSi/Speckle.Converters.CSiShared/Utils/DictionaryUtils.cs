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
      nestedDictionary = [];
      dictionary[key] = nestedDictionary;
    }

    return nestedDictionary;
  }

  /// <summary>
  /// Creates a standardized dictionary containing name, value and units.
  /// </summary>
  /// <param name="name">The name of the value</param>
  /// <param name="value">The numerical value</param>
  /// <param name="units">The units of the value</param>
  /// <returns>A dictionary with standardized keys for name, value and units</returns>
  /// <remarks>
  /// This just reduces repetion of creating dictionaries with the below keys.
  /// </remarks>
  public static Dictionary<string, object?> CreateValueUnitDictionary(string name, object value, string units) =>
    new()
    {
      ["name"] = name,
      ["value"] = value,
      ["units"] = units
    };
}
