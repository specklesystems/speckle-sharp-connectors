using Speckle.DoubleNumerics;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Common.Instances;

// Note: this was changed to a class with mutable props as in revit we need to pre-transform curves (native revit scaling does not support curves).
// public record LocalToGlobalMap(TraversalContext TraversalContext, Base AtomicObject, List<Matrix4x4> Matrix);

public class LocalToGlobalMap
{
  public LocalToGlobalMap(TraversalContext traversalContext, Base atomicObject, IReadOnlyCollection<Matrix4x4> matrix)
  {
    TraversalContext = traversalContext;
    AtomicObject = atomicObject;
    Matrix = matrix;
  }

  public TraversalContext TraversalContext { get; set; }
  public Base AtomicObject { get; set; }
  public IReadOnlyCollection<Matrix4x4> Matrix { get; set; }
  public List<string> InstanceChain { get; set; }
}
