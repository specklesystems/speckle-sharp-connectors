using TSD.API.Remoting.Solver;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal abstract class TsdElementForceResultsExtractorBase : TsdLoadingResultsExtractorBase<IElementEndForces>
{
  protected override Dictionary<string, object?> Build(IEnumerable<IElementEndForces> items)
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
