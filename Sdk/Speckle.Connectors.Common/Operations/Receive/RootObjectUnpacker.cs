﻿using Speckle.Objects.Other;
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
      TryGetColorProxies(root)
    );

  public IEnumerable<TraversalContext> GetObjectsToConvert(Base root) =>
    _traverseFunction.Traverse(root).Where(obj => obj.Current is not Collection);

  public List<ColorProxy>? TryGetColorProxies(Base root) => TryGetProxies<ColorProxy>(root, ProxyKeys.COLOR);

  public List<RenderMaterialProxy>? TryGetRenderMaterialProxies(Base root) =>
    TryGetProxies<RenderMaterialProxy>(root, ProxyKeys.RENDER_MATERIAL);

  public List<InstanceDefinitionProxy>? TryGetInstanceDefinitionProxies(Base root) =>
    TryGetProxies<InstanceDefinitionProxy>(root, ProxyKeys.INSTANCE_DEFINITION);

  public List<GroupProxy>? TryGetGroupProxies(Base root) => TryGetProxies<GroupProxy>(root, ProxyKeys.GROUP);

  public (
    List<TraversalContext> atomicObjects,
    List<TraversalContext> instanceComponents
  ) SplitAtomicObjectsAndInstances(IEnumerable<TraversalContext> objectsToSplit)
  {
    List<TraversalContext> atomicObjects = new();
    List<TraversalContext> instanceComponents = new();
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
    return (atomicObjects, instanceComponents);
  }

  private List<T>? TryGetProxies<T>(Base root, string key) => (root[key] as List<object>)?.Cast<T>().ToList();
}
