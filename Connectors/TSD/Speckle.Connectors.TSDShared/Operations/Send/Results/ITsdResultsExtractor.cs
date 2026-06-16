using TSD.API.Remoting.Solver;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed record TsdLoadingRef(Guid Id, string Name);

internal interface ITsdResultsExtractor
{
  string ResultsKey { get; }

  Task<Dictionary<string, object?>> GetResultsAsync(
    IAnalysis3DResults analysisResults,
    IReadOnlyList<TsdLoadingRef> loadings,
    CancellationToken cancellationToken
  );
}
