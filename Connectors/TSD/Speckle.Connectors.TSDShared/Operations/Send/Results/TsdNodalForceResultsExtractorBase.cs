using TSD.API.Remoting.Solver;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal abstract class TsdNodalForceResultsExtractorBase : TsdLoadingResultsExtractorBase<INodalForce>
{
  protected override Dictionary<string, object?> Build(IEnumerable<INodalForce> items)
  {
    var perNode = new Dictionary<string, object?>();
    foreach (var nodalForce in items)
    {
      perNode[nodalForce.NodeIndex.ToString()] = TsdResultValueBuilder.Force(nodalForce.Force);
    }

    return perNode;
  }
}
