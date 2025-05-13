using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Operations.Receive;

/// <summary>
/// Utility class to unpack layer structure from path of collections or property tree.
/// </summary>
public class TraversalContextUnpacker
{
  public IReadOnlyCollection<(Collection[] path, Base current)> GetAtomicObjectsWithPath(
    IEnumerable<TraversalContext> atomicObjects
  ) => atomicObjects.Select(o => (GetCollectionPath(o), o.Current)).ToList();

  public ICollection<(Collection[] path, IInstanceComponent instance)> GetInstanceComponentsWithPath(
    IEnumerable<TraversalContext> instanceComponents
  ) => instanceComponents.Select(o => (GetCollectionPath(o), (o.Current as IInstanceComponent)!)).ToList();

  /// <summary>
  /// Returns the collection path for the provided traversal context. If data is coming from a dynamic/non-dui3 connector, the collection path will be generated based on the property path. This function enforces that every collection will have a name and an application id.
  /// POC: this should be a util living somewhere else, most likely as an extension of the traversal context.
  /// </summary>
  /// <param name="context"></param>
  /// <returns></returns>
  public Collection[] GetCollectionPath(TraversalContext context)
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

    foreach (var collection in collectionBasedPath)
    {
      collection.applicationId ??= Guid.NewGuid().ToString();
    }

    return collectionBasedPath;
  }
}
