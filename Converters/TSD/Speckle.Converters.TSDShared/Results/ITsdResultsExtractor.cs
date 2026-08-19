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

public interface ITsdResultsExtractor
{
  string ResultsKey { get; }

  Task<Dictionary<string, object?>> GetResultsAsync(TsdResultsContext context, CancellationToken cancellationToken);
}
