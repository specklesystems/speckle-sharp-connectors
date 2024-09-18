using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Operations.Receive;

/// <summary>
/// Utility class to unpack layer structure from path of collections or property tree.
/// </summary>
public abstract class LayerPathUnpacker
{
  public List<(Collection[] path, Base current)> GetAtomicObjectsWithPath(
    IEnumerable<TraversalContext> atomicObjects
  ) => atomicObjects.Select(o => (GetLayerPath(o), o.Current)).ToList();

  public List<(Collection[] path, IInstanceComponent instance)> GetInstanceComponentsWithPath(
    IEnumerable<TraversalContext> instanceComponents
  ) => instanceComponents.Select(o => (GetLayerPath(o), (o.Current as IInstanceComponent)!)).ToList();

  public Collection[] GetLayerPath(TraversalContext context)
  {
    Collection[] collectionBasedPath = context.GetAscendantOfType<Collection>().Reverse().ToArray();

    if (collectionBasedPath.Length == 0)
    {
      collectionBasedPath = context
        .GetPropertyPath()
        .Reverse()
        .Select(o => new Collection() { applicationId = Guid.NewGuid().ToString(), name = o })
        .ToArray();
    }

    return collectionBasedPath;
  }
}
