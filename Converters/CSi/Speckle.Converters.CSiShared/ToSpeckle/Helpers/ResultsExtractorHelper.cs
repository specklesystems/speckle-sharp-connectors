namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public static class ResultsExtractorHelper
{
  /// <summary>
  /// Extracts the OutputCase from the nested dictionary structure.
  /// This is used for filtering against user selected load cases.
  /// </summary>
  /// <remarks>
  /// All database values are strings
  /// </remarks>
  public static string GetOutputCase(IReadOnlyDictionary<string, string> nestedDict, string selectedOutputCase) =>
    nestedDict.TryGetValue(selectedOutputCase, out var outputCase) ? outputCase : string.Empty;

  /// <summary>
  /// Safely parses a value to double, returning 0.0 if parsing fails.
  /// Database returns all values as strings, so conversion is needed.
  /// </summary>
  public static double TryParseDouble(object? value)
  {
    if (value == null)
    {
      throw new InvalidOperationException("Cannot parse null value to double in story force results");
    }

    var stringValue = value.ToString();
    if (string.IsNullOrEmpty(stringValue))
    {
      throw new InvalidOperationException("Cannot parse empty string to double in story force results");
    }

    if (!double.TryParse(stringValue, out double result))
    {
      throw new InvalidOperationException($"Failed to parse '{stringValue}' as double in story force results");
    }

    return result;
  }
}
