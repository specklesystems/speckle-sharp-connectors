using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

/// <summary>
/// Implementations handle specific result types (frame forces, joint reactions, etc.) and
/// transform raw API data into hierarchical dictionary structures.
/// </summary>
public interface IApplicationResultsExtractor
{
  /// <summary>
  /// Gets the key name used to store these results in the root commit object.
  /// </summary>
  /// <example>"FrameForces", "JointReactions", "BaseReactions"</example>
  string ResultsKey { get; }

  /// <summary>
  /// Gets the configuration defining how to process raw API arrays into hierarchical structure.
  /// Specifies grouping hierarchy and result value keys.
  /// </summary>
  ResultsConfiguration Configuration { get; }

  /// <summary>
  /// Extracts analysis results for the specified objects and processes them into hierarchical format.
  /// </summary>
  /// <param name="objectNames">
  /// Names of objects to extract results for. Some extractors don't extract object specific results, hence null.
  /// </param>
  Dictionary<string, object> GetResults(IEnumerable<string>? objectNames = null);
}
