using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

/// <summary>
/// Processes block instances and their nested definitions for publish.
/// Handles nested definitions and depth tracking (injected InstanceObjectsManager).
/// </summary>
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
  /// Processes a <see cref="SpeckleBlockInstanceWrapper"/> by tracking it in InstanceObjectsManager and recursively
  /// processing its definition. Handles depth calculation for nested block hierarchies.
  /// </summary>
  /// <param name="blockInstance">The block instance to process</param>
  /// <param name="depth">Current nesting depth (0 = top level, increases for nested instances)</param>
  public List<SpeckleObjectWrapper>? ProcessInstance(SpeckleBlockInstanceWrapper? blockInstance, int depth = 0)
  {
    if (blockInstance?.Definition == null)
    {
      return null;
    }

    blockInstance.ApplicationId ??= Guid.NewGuid().ToString();
    var instanceId = blockInstance.ApplicationId;

    blockInstance.InstanceProxy.maxDepth = depth;
    _instanceObjectsManager.AddInstanceProxy(instanceId, blockInstance.InstanceProxy);

    return ProcessDefinition(blockInstance.Definition, depth);
  }

  /// <summary>
  /// Processes a block definition, adding it and its objects to InstanceObjectsManager.
  /// Updates maxDepth for existing definitions when encountered at greater depths.
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
    var currentObjectIds = new List<string>(); // Track current object IDs for proxy update

    foreach (var obj in definition.Objects)
    {
      if (obj.ApplicationId == null) // we should be loud about this. If gone through all casting etc. this should be complete!
      {
        throw new InvalidOperationException(
          $"Object in block definition '{definition.Name}' missing ApplicationId during send operation. This indicates a processing pipeline error."
        );
      }

      objectsToAdd.Add(obj);
      currentObjectIds.Add(obj.ApplicationId); // Collect current ID
      _instanceObjectsManager.AddAtomicObject(obj.ApplicationId, obj);

      if (obj is SpeckleBlockInstanceWrapper nestedInstance)
      {
        var nestedObjects = ProcessInstance(nestedInstance, depth + 1);
        if (nestedObjects != null)
        {
          objectsToAdd.AddRange(nestedObjects);
        }
      }
    }

    // Add definition to InstanceObjectsManager
    definition.InstanceDefinitionProxy.objects = currentObjectIds;
    definition.InstanceDefinitionProxy.maxDepth = depth;
    _instanceObjectsManager.AddDefinitionProxy(definitionId, definition.InstanceDefinitionProxy);
    InstanceDefinitionProxies[definitionId] = definition.InstanceDefinitionProxy;

    return objectsToAdd;
  }
}
