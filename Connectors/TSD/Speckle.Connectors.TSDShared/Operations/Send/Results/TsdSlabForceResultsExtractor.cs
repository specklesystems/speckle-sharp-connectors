using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed class TsdSlabForceResultsExtractor : TsdNodalForceResultsExtractorBase
{
  public override string ResultsKey => "slabForces";

  protected override async Task<IEnumerable<INodalForce>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetNodalSlabForcesAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);
}
