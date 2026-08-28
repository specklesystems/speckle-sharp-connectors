using Speckle.Sdk;
using TSD.API.Remoting.Structure;

namespace Speckle.Converters.TSDShared.Results;

/// <summary>
/// Translates solver node indices into the construction point indices that the rest of the published payload uses.
/// </summary>
/// <remarks>
/// TSD numbers solver nodes independently of construction points, so the raw node index carried by a solver result
/// does not identify the same node as, say, a member's I-End Node. Publishing nodal results under the raw solver index
/// therefore attaches a valid result to the wrong node. A solver node can back several coincident construction points,
/// and mesh nodes (for example along a supported slab or wall edge) have no construction point at all - those keep
/// their solver index behind <see cref="SOLVER_NODE_PREFIX"/> so that the two index spaces cannot collide.
/// </remarks>
public sealed class TsdNodeIndexMap
{
  /// <summary>
  /// Marks a key that is still a solver node index because the node has no construction point in the model.
  /// </summary>
  public const string SOLVER_NODE_PREFIX = "solverNode:";

  private readonly Dictionary<int, List<string>> _constructionPointKeys;

  private TsdNodeIndexMap(Dictionary<int, List<string>> constructionPointKeys)
  {
    _constructionPointKeys = constructionPointKeys;
  }

  /// <summary>
  /// Reads the model's construction points and indexes them by the solver node each one resolves to.
  /// </summary>
  public static async Task<TsdNodeIndexMap> CreateAsync(IModel model, CancellationToken cancellationToken)
  {
    var constructionPoints = await model.GetConstructionPointsAsync(null, cancellationToken).ConfigureAwait(false);

    var constructionPointKeys = new Dictionary<int, List<string>>();
    foreach (var constructionPoint in constructionPoints ?? Enumerable.Empty<IConstructionPoint>())
    {
      if (TryGetSolverNodeIndex(constructionPoint) is not int solverNodeIndex)
      {
        continue;
      }

      if (!constructionPointKeys.TryGetValue(solverNodeIndex, out var keys))
      {
        keys = new List<string>(1);
        constructionPointKeys[solverNodeIndex] = keys;
      }

      keys.Add(constructionPoint.Index.ToString());
    }

    return new TsdNodeIndexMap(constructionPointKeys);
  }

  /// <summary>
  /// Returns every key a result for <paramref name="solverNodeIndex"/> should be published under.
  /// </summary>
  public IReadOnlyList<string> GetNodeKeys(int solverNodeIndex) =>
    _constructionPointKeys.TryGetValue(solverNodeIndex, out var keys)
      ? keys
      : new[] { $"{SOLVER_NODE_PREFIX}{solverNodeIndex}" };

  private static int? TryGetSolverNodeIndex(IConstructionPoint constructionPoint)
  {
    try
    {
      return constructionPoint.SolverNodeIndex.IsApplicable ? constructionPoint.SolverNodeIndex.Value : null;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // a construction point that is not part of the solver model simply contributes nothing to the map
      return null;
    }
  }
}
