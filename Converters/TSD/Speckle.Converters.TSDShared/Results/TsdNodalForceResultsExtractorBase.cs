using TSD.API.Remoting.Solver;

namespace Speckle.Converters.TSDShared.Results;

public abstract class TsdNodalForceResultsExtractorBase : TsdLoadingResultsExtractorBase<INodalForce>
{
  protected override Dictionary<string, object?> Build(IEnumerable<INodalForce> items, TsdResultsContext context)
  {
    var perNode = new Dictionary<string, object?>();
    foreach (var nodalForce in items)
    {
      foreach (var nodeKey in context.Nodes.GetNodeKeys(nodalForce.NodeIndex))
      {
        perNode[nodeKey] = TsdResultValueBuilder.Force(nodalForce.Force);
      }
    }

    return perNode;
  }
}
