using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Common.Builders;

public static class TraversalExtensions
{
  [Obsolete(
    "Do not use - we're doing multi stage receives and this confuses things. Report progress as appropriate from the connector side.",
    true
  )]
  public static IEnumerable<TraversalContext> TraverseWithProgress(
    this GraphTraversal traversalFunction,
    Base rootObject,
    Action<string, double?>? onOperationProgressed,
    CancellationToken cancellationToken = default
  )
  {
    var traversalGraph = traversalFunction.Traverse(rootObject).ToArray();
    int count = 0;
    foreach (var tc in traversalGraph)
    {
      cancellationToken.ThrowIfCancellationRequested();

      yield return tc;

      onOperationProgressed?.Invoke("Converting", (double)++count / traversalGraph.Length);
    }
  }
}
