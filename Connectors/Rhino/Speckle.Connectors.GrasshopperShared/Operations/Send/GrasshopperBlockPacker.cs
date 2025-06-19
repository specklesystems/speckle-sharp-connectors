using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

/// <summary>
/// Processes block instances (and underlying definitions) for publish.
/// </summary>
/// <remarks>
/// Only <see cref="SpeckleCollectionWrapper"/> can be published. Collections accept <see cref="SpeckleObjectWrapper"/>
/// and <see cref="SpeckleBlockInstanceWrapper"/>. They (for now) explicitly don't allow <see cref="SpeckleBlockDefinitionWrapper"/>.
/// Therefore, the case where a definition is directly provided won't occur.
/// </remarks>
internal sealed class GrasshopperBlockPacker
{
  private readonly IInstanceObjectsManager<SpeckleObjectWrapper, List<string>> _instanceObjectsManager;

  public GrasshopperBlockPacker(IInstanceObjectsManager<SpeckleObjectWrapper, List<string>> instanceObjectsManager)
  {
    _instanceObjectsManager = instanceObjectsManager;
  }

  /// <summary>
  /// Stores a map of instance definition id to instance definition proxy
  /// </summary>
  /// <remarks>
  /// Storing <see cref="InstanceDefinitionProxy"/> directly and not the wrapper (matching Rhino).
  /// </remarks>
  public Dictionary<string, InstanceDefinitionProxy> InstanceDefinitionProxies { get; } = [];

  /// <summary>
  /// Processes a <see cref="SpeckleBlockInstanceWrapper"/> by validating inputs before delegating to private method.
  /// Returns the <see cref="SpeckleObjectWrapper"/>s that make up the definition(s). These need to be added to the collection.
  /// </summary>
  /// /// <param name="depth">Current nesting depth (0 = top level, increases for nested instances)</param>
  public List<SpeckleObjectWrapper>? ProcessInstance(SpeckleBlockInstanceWrapper? blockInstance, int depth = 0)
  {
    if (blockInstance?.Definition == null)
    {
      return null;
    }

    var instanceId = blockInstance.ApplicationId ?? Guid.NewGuid().ToString(); // Safety net - final validation before object tracking

    blockInstance.InstanceProxy.maxDepth = depth;
    _instanceObjectsManager.AddInstanceProxy(instanceId, blockInstance.InstanceProxy);

    return ProcessDefinition(blockInstance.Definition, depth);
  }

  /// <summary>
  /// Processes a <see cref="SpeckleBlockDefinitionWrapper"/> and adds it to the current collection (if not already present).
  /// </summary>
  private List<SpeckleObjectWrapper>? ProcessDefinition(SpeckleBlockDefinitionWrapper definition, int depth = 0)
  {
    // Use wrapper's id as definitive identifier. Create if empty.
    definition.ApplicationId ??= Guid.NewGuid().ToString();
    string definitionId = definition.ApplicationId;

    // Check if already processed using InstanceObjectsManager
    if (
      _instanceObjectsManager.TryGetInstanceDefinitionProxy(definitionId, out InstanceDefinitionProxy? definitionProxy)
    )
    {
      int depthDifference = depth - definitionProxy.maxDepth;
      if (depthDifference > 0)
      {
        // Use InstanceObjectsManager to update max depth
        _instanceObjectsManager.UpdateChildrenMaxDepth(definitionProxy, depthDifference);
      }

      return null; // this prevents infinite recursion
    }

    // Process objects recursively
    var objectsToAdd = new List<SpeckleObjectWrapper>();
    foreach (var obj in definition.Objects)
    {
      if (obj is SpeckleBlockInstanceWrapper nestedInstance)
      {
        objectsToAdd.Add(nestedInstance);
        _instanceObjectsManager.AddAtomicObject(
          nestedInstance.ApplicationId ?? Guid.NewGuid().ToString(),
          nestedInstance
        );

        var nestedObjects = ProcessInstance(nestedInstance, depth + 1);
        if (nestedObjects != null)
        {
          objectsToAdd.AddRange(nestedObjects);
        }
      }
      else
      {
        objectsToAdd.Add(obj);
        _instanceObjectsManager.AddAtomicObject(obj.ApplicationId ?? Guid.NewGuid().ToString(), obj);
      }
    }

    // Add definition to InstanceObjectsManager
    definition.InstanceDefinitionProxy.maxDepth = depth;
    _instanceObjectsManager.AddDefinitionProxy(definitionId, definition.InstanceDefinitionProxy);
    InstanceDefinitionProxies[definitionId] = definition.InstanceDefinitionProxy;

    return objectsToAdd;
  }
}
