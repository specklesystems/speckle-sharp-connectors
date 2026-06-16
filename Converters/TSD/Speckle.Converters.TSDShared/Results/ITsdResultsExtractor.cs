using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public sealed record TsdLoadingRef(Guid Id, string Name);

public interface ITsdResultsExtractor
{
  string ResultsKey { get; }

  Task<Dictionary<string, object?>> GetResultsAsync(
    IAnalysis3DResults analysisResults,
    IReadOnlyList<TsdLoadingRef> loadings,
    CancellationToken cancellationToken
  );
}
