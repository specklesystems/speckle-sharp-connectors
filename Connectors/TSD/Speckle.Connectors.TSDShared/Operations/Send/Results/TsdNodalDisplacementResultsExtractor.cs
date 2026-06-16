using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed class TsdNodalDisplacementResultsExtractor : TsdLoadingResultsExtractorBase<INodalDisplacement>
{
  public override string ResultsKey => "nodalDisplacements";

  protected override async Task<IEnumerable<INodalDisplacement>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetNodalDisplacementsAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);

  protected override Dictionary<string, object?> Build(IEnumerable<INodalDisplacement> items)
  {
    var perNode = new Dictionary<string, object?>();
    foreach (var nodalDisplacement in items)
    {
      perNode[nodalDisplacement.NodeIndex.ToString()] = TsdResultValueBuilder.Displacement(
        nodalDisplacement.Displacement
      );
    }

    return perNode;
  }
}
