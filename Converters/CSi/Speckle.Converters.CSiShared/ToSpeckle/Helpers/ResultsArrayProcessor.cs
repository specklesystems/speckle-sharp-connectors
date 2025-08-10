using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Processes parallel arrays from analysis results into hierarchical dictionary structures.
/// Uses configuration to determine grouping hierarchy and result values.
/// </summary>
public class ResultsArrayProcessor
{
  /// <summary>
  /// Transforms parallel arrays into nested dictionary hierarchy based on configuration.
  /// </summary>
  /// <param name="rawArrays">Dictionary of array names to their values (all arrays must have same length)</param>
  /// <param name="config">Configuration defining grouping keys and result keys</param>
  /// <returns>Nested dictionary following GroupingKeys hierarchy with ResultKeys as leaf values</returns>
  public Dictionary<string, object> ProcessArrays(Dictionary<string, object> rawArrays, ResultsConfiguration config)
  {
    // get array length from first array (all should be same length)
    var firstArray = rawArrays.Values.FirstOrDefault();
    if (firstArray is not Array array || array.Length == 0)
    {
      return new Dictionary<string, object>();
    }

    int arrayLength = array.Length;

    // create indices for all rows
    var indices = Enumerable.Range(0, arrayLength);

    // build the hierarchy recursively
    return BuildHierarchy(indices, rawArrays, config.GroupingKeys, config.ResultKeys, 0);
  }

  private Dictionary<string, object> BuildHierarchy(
    IEnumerable<int> indices,
    Dictionary<string, object> rawArrays,
    IReadOnlyList<string> groupingKeys,
    IReadOnlyList<string> resultKeys,
    int level
  )
  {
    // Base case: we've processed all grouping levels, create result values
    if (level >= groupingKeys.Count)
    {
      var results = new Dictionary<string, object>();
      var firstIndex = indices.First();

      foreach (var resultKey in resultKeys)
      {
        if (rawArrays.TryGetValue(resultKey, out var array) && array is Array resultArray)
        {
          var value = resultArray.GetValue(firstIndex);
          if (value != null)
          {
            results[resultKey] = value;
          }
        }
      }

      return results;
    }

    // Get current grouping key
    var currentKey = groupingKeys[level];
    string actualKey;
    string? wrapperName = null;

    // Check for "Wrap:" prefix
    if (currentKey.StartsWith("Wrap:"))
    {
      actualKey = currentKey["Wrap:".Length..];
      wrapperName = actualKey; // Use the actual key name as wrapper
    }
    else
    {
      actualKey = currentKey;
    }

    if (!rawArrays.TryGetValue(actualKey, out var groupingArray) || groupingArray is not Array currentArray)
    {
      throw new ArgumentException($"Grouping key '{actualKey}' not found in raw arrays");
    }

    // Group indices by the current key's values
    var grouped = indices
      .GroupBy(i => currentArray.GetValue(i)?.ToString() ?? string.Empty)
      .ToDictionary(g => g.Key, g => (object)BuildHierarchy(g, rawArrays, groupingKeys, resultKeys, level + 1));

    // Wrap if needed
    if (wrapperName != null)
    {
      return new Dictionary<string, object> { [wrapperName] = grouped };
    }

    return grouped;
  }
}
