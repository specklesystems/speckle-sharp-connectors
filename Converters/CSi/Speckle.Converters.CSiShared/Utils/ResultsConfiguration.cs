namespace Speckle.Converters.CSiShared.Utils;

/// <summary>
/// Configuration used by all IResultsExtractor to sort results arrays into a hierarchical structure.
/// </summary>
/// <param name="GroupingKeys">Keys defining the hierarchy levels for grouping results, in order from top-level to bottom-level</param>
/// <param name="ResultKeys">Keys for the final result values stored at the leaf level of the hierarchy</param>
public record ResultsConfiguration(IReadOnlyList<string> GroupingKeys, IReadOnlyList<string> ResultKeys);
