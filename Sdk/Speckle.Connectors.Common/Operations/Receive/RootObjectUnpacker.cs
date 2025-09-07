using Speckle.Objects.Other;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Common.Operations.Receive;

/// <summary>
/// Unpacker root object for receive operation.
/// </summary>
public class RootObjectUnpacker
{
  private readonly GraphTraversal _traverseFunction;

  public RootObjectUnpacker(GraphTraversal traverseFunction)
  {
    _traverseFunction = traverseFunction;
  }

  public RootObjectUnpackerResult Unpack(Base root) =>
    new(
      GetObjectsToConvert(root),
      TryGetInstanceDefinitionProxies(root),
      TryGetGroupProxies(root),
      TryGetRenderMaterialProxies(root),
      TryGetColorProxies(root),
      TryGetLevelProxies(root)
    );

  public IReadOnlyCollection<TraversalContext> GetObjectsToConvert(Base root) =>
    _traverseFunction.Traverse(root).Where(obj => obj.Current is not Collection).Reverse().ToArray();

  public IReadOnlyCollection<ColorProxy>? TryGetColorProxies(Base root) =>
    TryGetProxies<ColorProxy>(root, ProxyKeys.COLOR);

  public IReadOnlyCollection<RenderMaterialProxy>? TryGetRenderMaterialProxies(Base root) =>
    TryGetProxies<RenderMaterialProxy>(root, ProxyKeys.RENDER_MATERIAL);

  public IReadOnlyCollection<InstanceDefinitionProxy>? TryGetInstanceDefinitionProxies(Base root) =>
    TryGetProxies<InstanceDefinitionProxy>(root, ProxyKeys.INSTANCE_DEFINITION);

  public IReadOnlyCollection<GroupProxy>? TryGetGroupProxies(Base root) =>
    TryGetProxies<GroupProxy>(root, ProxyKeys.GROUP);

  public IReadOnlyCollection<LevelProxy>? TryGetLevelProxies(Base root) =>
    TryGetProxies<LevelProxy>(root, ProxyKeys.LEVEL);

  public (
    IReadOnlyCollection<TraversalContext> atomicObjects,
    IReadOnlyCollection<TraversalContext> instanceComponents
  ) SplitAtomicObjectsAndInstances(IEnumerable<TraversalContext> objectsToSplit)
  {
    HashSet<TraversalContext> atomicObjects = [];
    HashSet<TraversalContext> instanceComponents = [];
    foreach (TraversalContext tc in objectsToSplit)
    {
      if (tc.Current is IInstanceComponent)
      {
        instanceComponents.Add(tc);
      }
      else
      {
        atomicObjects.Add(tc);
      }
    }
    return (atomicObjects.ToArray(), instanceComponents.ToArray());
  }

  private IReadOnlyCollection<T>? TryGetProxies<T>(Base root, string key) =>
    (root[key] as List<object>)?.Cast<T>().ToList();
}
