using Speckle.Objects.Data;
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
      TryGetLevelProxies(root),
      TryGetCameras(root)
    );

  private IReadOnlyCollection<TraversalContext> GetObjectsToConvert(Base root) =>
    _traverseFunction.Traverse(root).Where(obj => obj.Current is not Collection).ToArray();

  private IReadOnlyCollection<ColorProxy>? TryGetColorProxies(Base root) =>
    TryGetProxies<ColorProxy>(root, ProxyKeys.COLOR);

  private IReadOnlyCollection<RenderMaterialProxy>? TryGetRenderMaterialProxies(Base root) =>
    TryGetProxies<RenderMaterialProxy>(root, ProxyKeys.RENDER_MATERIAL);

  private IReadOnlyCollection<InstanceDefinitionProxy>? TryGetInstanceDefinitionProxies(Base root) =>
    TryGetProxies<InstanceDefinitionProxy>(root, ProxyKeys.INSTANCE_DEFINITION);

  private IReadOnlyCollection<GroupProxy>? TryGetGroupProxies(Base root) =>
    TryGetProxies<GroupProxy>(root, ProxyKeys.GROUP);

  private IReadOnlyCollection<LevelProxy>? TryGetLevelProxies(Base root) =>
    TryGetProxies<LevelProxy>(root, ProxyKeys.LEVEL);

  private IReadOnlyCollection<Camera>? TryGetCameras(Base root) =>
    (root[RootKeys.VIEW] as IEnumerable<object>)?.OfType<Camera>().ToList();

  public (
    IReadOnlyCollection<TraversalContext> atomicObjects,
    IReadOnlyCollection<TraversalContext> instanceComponents
  ) SplitAtomicObjectsAndInstances(IEnumerable<TraversalContext> objectsToSplit)
  {
    List<TraversalContext> atomicObjects = [];
    List<TraversalContext> instanceComponents = [];
    foreach (TraversalContext tc in objectsToSplit)
    {
      if (tc.Current is IInstanceComponent)
      {
        instanceComponents.Add(tc); // handles actual blocks / instances
      }
      else
      {
        atomicObjects.Add(tc); // handles DataObject which INCLUDES DataObject with proxified displayValue(s)
      }

      if (tc.Current is DataObject dataObject)
      {
        foreach (var displayValue in dataObject.displayValue)
        {
          if (displayValue is IInstanceComponent)
          {
            instanceComponents.Add(new TraversalContext(displayValue, parent: tc));
          }
        }
      }
    }
    return (atomicObjects, instanceComponents);
  }

  private IReadOnlyCollection<T>? TryGetProxies<T>(Base root, string key) =>
    (root[key] as List<object>)?.Cast<T>().ToList();
}
