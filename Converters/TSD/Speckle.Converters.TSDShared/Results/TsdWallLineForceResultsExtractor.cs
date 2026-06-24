using TSD.API.Remoting.Loading;
using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public sealed class TsdWallLineForceResultsExtractor : TsdLoadingResultsExtractorBase<IWallLineForces>
{
  public override string ResultsKey => "wallLineForces";

  protected override async Task<IEnumerable<IWallLineForces>?> FetchAsync(
    IAnalysis3DResults analysisResults,
    Guid loadingId,
    CancellationToken cancellationToken
  ) =>
    await analysisResults
      .GetWallLineForcesAsync(loadingId, LoadingResultType.Base, null, cancellationToken)
      .ConfigureAwait(false);

  protected override Dictionary<string, object?> Build(IEnumerable<IWallLineForces> items)
  {
    var perWallLine = new Dictionary<string, object?>();
    foreach (var wallLine in items)
    {
      var stations = new Dictionary<string, object?>();
      foreach (var station in wallLine.StationForces)
      {
        stations[station.Key.ToString()] = new Dictionary<string, object?>
        {
          ["left"] = TsdResultValueBuilder.Force(station.Value.LeftForce),
          ["right"] = TsdResultValueBuilder.Force(station.Value.RightForce),
        };
      }

      if (stations.Count > 0)
      {
        perWallLine[wallLine.WallLineIndex.ToString()] = stations;
      }
    }

    return perWallLine;
  }
}
