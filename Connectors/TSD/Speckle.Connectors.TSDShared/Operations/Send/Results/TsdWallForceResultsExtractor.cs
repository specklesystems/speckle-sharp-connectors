using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed class TsdWallForceResultsExtractor : TsdNodalForceResultsExtractorBase
{
  public override string ResultsKey => "wallForces";

  protected override async Task<IEnumerable<INodalForce>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetNodalWallForcesAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);
}
