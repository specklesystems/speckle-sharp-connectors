using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public sealed record TsdLoadingRef(Guid Id, string Name);

/// <summary>
/// The inputs shared by every results extractor for a single send.
/// </summary>
public sealed record TsdResultsContext(
  IAnalysis3DResults AnalysisResults,
  IReadOnlyList<TsdLoadingRef> Loadings,
  TsdNodeIndexMap Nodes
);

<<<<<<< HEAD
=======
/// <summary>
/// One send's extracted results: the nested result tree, plus the map that resolves the solver element indices in it
/// back to published objects. The map travels with the tree because the tree alone cannot be joined to geometry.
/// </summary>
public sealed record TsdResultsPayload(Dictionary<string, object?> Tree, TsdElementIndexMap Elements);

>>>>>>> big-truck
public interface ITsdResultsExtractor
{
  string ResultsKey { get; }

  Task<Dictionary<string, object?>> GetResultsAsync(TsdResultsContext context, CancellationToken cancellationToken);
}
