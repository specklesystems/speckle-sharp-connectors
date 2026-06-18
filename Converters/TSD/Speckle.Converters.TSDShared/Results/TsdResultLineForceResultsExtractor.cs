using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public sealed class TsdResultLineForceResultsExtractor : TsdLoadingResultsExtractorBase<IResultLineForces>
{
  public override string ResultsKey => "resultLineForces";

  protected override async Task<IEnumerable<IResultLineForces>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetResultLineForcesAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);

  protected override Dictionary<string, object?> Build(IEnumerable<IResultLineForces> items)
  {
    var perLine = new Dictionary<string, object?>();
    foreach (var line in items)
    {
      perLine[line.ResultLineIndex.ToString()] = new Dictionary<string, object?>
      {
        ["left"] = TsdResultValueBuilder.Force(line.LeftForce),
        ["right"] = TsdResultValueBuilder.Force(line.RightForce),
      };
    }

    return perLine;
  }
}
