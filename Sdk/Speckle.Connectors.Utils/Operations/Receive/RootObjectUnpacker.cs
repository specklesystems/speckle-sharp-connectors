using Speckle.Objects.Other;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Utils.Operations.Receive;

/// <summary>
/// Unpacker root object for receive operation.
/// </summary>
public class RootObjectUnpacker
{
  private readonly GraphTraversal _traverseFunction;
  private const string COLOR_PROXIES_KEY = "colorProxies";
  private const string RENDER_MATERIAL_PROXIES_KEY = "renderMaterialProxies";
  private const string INSTANCE_DEFINITION_PROXIES_KEY = "instanceDefinitionProxies";
  private const string GROUP_PROXIES_KEY = "groupProxies";

  public RootObjectUnpacker(GraphTraversal traverseFunction)
  {
    _traverseFunction = traverseFunction;
  }

  public IEnumerable<TraversalContext> GetObjectsToConvert(Base root) =>
    _traverseFunction.Traverse(root).Where(obj => obj.Current is not Collection);

  public List<ColorProxy>? TryGetColorProxies(Base root) => TryGetProxies<ColorProxy>(root, COLOR_PROXIES_KEY);

  public List<RenderMaterialProxy>? TryGetRenderMaterialProxies(Base root) =>
    TryGetProxies<RenderMaterialProxy>(root, RENDER_MATERIAL_PROXIES_KEY);

  public List<InstanceDefinitionProxy>? TryGetInstanceDefinitionProxies(Base root) =>
    TryGetProxies<InstanceDefinitionProxy>(root, INSTANCE_DEFINITION_PROXIES_KEY);

  public List<GroupProxy>? TryGetGroupProxies(Base root) => TryGetProxies<GroupProxy>(root, GROUP_PROXIES_KEY);

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
