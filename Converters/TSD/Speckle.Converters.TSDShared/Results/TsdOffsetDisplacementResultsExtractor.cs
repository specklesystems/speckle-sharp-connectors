using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public sealed class TsdOffsetDisplacementResultsExtractor : TsdLoadingResultsExtractorBase<IElementOffsetDisplacements>
{
  public override string ResultsKey => "offsetDisplacements";

  protected override async Task<IEnumerable<IElementOffsetDisplacements>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetOffsetDisplacementsAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);

  protected override Dictionary<string, object?> Build(IEnumerable<IElementOffsetDisplacements> items)
  {
    var perElement = new Dictionary<string, object?>();
    foreach (var elementDisplacements in items)
    {
      perElement[elementDisplacements.ElementIndex.ToString()] = new Dictionary<string, object?>
      {
        ["start"] = TsdResultValueBuilder.Displacement(elementDisplacements.StartOffsetDisplacement),
        ["end"] = TsdResultValueBuilder.Displacement(elementDisplacements.EndOffsetDisplacement),
      };
    }

    return perElement;
  }
}
