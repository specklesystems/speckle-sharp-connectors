using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public abstract class TsdElementForceResultsExtractorBase : TsdLoadingResultsExtractorBase<IElementEndForces>
{
  protected override Dictionary<string, object?> Build(IEnumerable<IElementEndForces> items, TsdResultsContext context)
  {
    var perElement = new Dictionary<string, object?>();
    foreach (var elementForces in items)
    {
      perElement[elementForces.ElementIndex.ToString()] = new Dictionary<string, object?>
      {
        ["start"] = TsdResultValueBuilder.Force(elementForces.StartForce),
        ["end"] = TsdResultValueBuilder.Force(elementForces.EndForce),
      };
    }

    return perElement;
  }
}
