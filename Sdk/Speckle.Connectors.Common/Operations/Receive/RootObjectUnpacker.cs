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
      TryGetLevelProxies(root)
    );

  public IReadOnlyCollection<TraversalContext> GetObjectsToConvert(Base root) =>
    _traverseFunction.Traverse(root).Where(obj => obj.Current is not Collection).ToArray();

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

  /// <summary>
  /// POC!!! super hacky, we need an official way to differentiate between atomic instances, display value instances, atomic instance definitions, and display value instance definitions.
  /// This should be reflected on the root collection.
  /// </summary>
  /// <param name="objectsToSplit"></param>
  /// <returns></returns>
  public (
    IReadOnlyCollection<TraversalContext> atomicNonInstanceObjects,
    IReadOnlyCollection<TraversalContext> instanceComponents,
    IReadOnlyCollection<TraversalContext> atomicNonInstanceObjectsWithInstanceComponents
  ) SplitAtomicObjectsAndInstances(IEnumerable<TraversalContext> objectsToSplit)
  {
    List<TraversalContext> atomicObjectsWithoutInstanceComponents = [];
    List<TraversalContext> instanceComponents = [];
    List<TraversalContext> atomicObjectsWithInstanceComponents = [];
    foreach (TraversalContext tc in objectsToSplit)
    {
      if (tc.Current is IInstanceComponent)
      {
        instanceComponents.Add(tc);
      }
      else
      {
        if (tc.Current is DataObject dataObject)
        {
          bool containsInstanceComponents = false;
          foreach (var displayValue in dataObject.displayValue)
          {
            if (displayValue is IInstanceComponent)
            {
              containsInstanceComponents = true;
              instanceComponents.Add(new TraversalContext(displayValue, parent: tc));
            }
          }

          if (containsInstanceComponents)
          {
            atomicObjectsWithInstanceComponents.Add(tc);
          }
          else
          {
            atomicObjectsWithoutInstanceComponents.Add(tc);
          }
        }
        else
        {
          atomicObjectsWithoutInstanceComponents.Add(tc);
        }
      }
    }

    return (atomicObjectsWithoutInstanceComponents, instanceComponents, atomicObjectsWithInstanceComponents);
  }

  private IReadOnlyCollection<T>? TryGetProxies<T>(Base root, string key) =>
    (root[key] as List<object>)?.Cast<T>().ToList();
}
