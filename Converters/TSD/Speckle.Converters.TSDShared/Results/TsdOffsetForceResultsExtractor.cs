using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public sealed class TsdOffsetForceResultsExtractor : TsdElementForceResultsExtractorBase
{
  public override string ResultsKey => "offsetForces";

  protected override async Task<IEnumerable<IElementEndForces>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetOffsetForcesAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);
}
