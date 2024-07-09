using Speckle.Core.Models.Instances;

namespace Speckle.Connectors.Utils.Instances;

/// <summary>
/// Abstract class to cover common functionalities of inheritors.
/// </summary>
public abstract class InstanceObjectsManager<THostObjectType, TAppIdMapValueType>
  : IInstanceObjectsManager<THostObjectType, TAppIdMapValueType>
{
  protected readonly Dictionary<string, InstanceProxy> InstanceProxies = new();
  protected readonly Dictionary<string, List<InstanceProxy>> InstanceProxiesByDefinitionId = new();
  protected readonly Dictionary<string, InstanceDefinitionProxy> DefinitionProxies = new();
  protected readonly Dictionary<string, THostObjectType> FlatAtomicObjects = new();

  public abstract UnpackResult<THostObjectType> UnpackSelection(IEnumerable<THostObjectType> objects);

  public abstract BakeResult BakeInstances(
    List<(string[] layerPath, IInstanceComponent obj)> instanceComponents,
    Dictionary<string, TAppIdMapValueType> applicationIdMap,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  );

  public abstract void PurgeInstances(string namePrefix);

  /// <summary>
  /// Update children max depths whenever definition proxy is found on the unpacked dictionary (<see cref="DefinitionProxies"/>).
  /// Even if definition unpacked before, max depth of its children must be updated if upcoming max depth is higher than existing one.
  /// </summary>
  /// <param name="definitionProxy"> Definition proxy to update max depth of its children.</param>
  /// <param name="depthDifference"> Value to increase max depth of children.</param>
  protected void UpdateChildrenMaxDepth(InstanceDefinitionProxy definitionProxy, int depthDifference)
  {
    // Increase depth of definition
    definitionProxy.MaxDepth += depthDifference;

    // Find instance proxies of given definition
    var definitionInstanceProxies = definitionProxy.Objects
      .Where(id => InstanceProxies.TryGetValue(id, out _))
      .Select(id => InstanceProxies[id])
      .ToList();

    // Break the loop if no instance proxy found under definition.
    if (!definitionInstanceProxies.Any())
    {
      return;
    }

    var subDefinitions = new Dictionary<string, InstanceDefinitionProxy>();
    foreach (InstanceProxy instanceProxy in definitionInstanceProxies)
    {
      // Increase depth of instance
      instanceProxy.MaxDepth += depthDifference;
      // Collect sub definitions
      subDefinitions[instanceProxy.DefinitionId] = DefinitionProxies[instanceProxy.DefinitionId];
    }

    // Iterate through sub definitions
    foreach (var subDefinition in subDefinitions.Values)
    {
      UpdateChildrenMaxDepth(subDefinition, depthDifference);
    }
  }
}
