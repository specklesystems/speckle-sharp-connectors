using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

public class InstanceObjectsManager<THostObjectType, TAppIdMapValueType>
  : IInstanceObjectsManager<THostObjectType, TAppIdMapValueType>
{
  private readonly Dictionary<string, InstanceProxy> _instanceProxies = new();
  private readonly Dictionary<string, List<InstanceProxy>> _instanceProxiesByDefinitionId = new();
  private readonly Dictionary<string, InstanceDefinitionProxy> _definitionProxies = new();
  private readonly Dictionary<string, THostObjectType> _flatAtomicObjects = new();
  private readonly Dictionary<string, THostObjectType> _flatAtomicDefinitionObjects = new();

  public void AddInstanceProxy(string objectId, InstanceProxy instanceProxy) =>
    _instanceProxies[objectId] = instanceProxy;

  public void AddDefinitionProxy(string objectId, InstanceDefinitionProxy instanceDefinitionProxy) =>
    _definitionProxies[objectId] = instanceDefinitionProxy;

  public void AddAtomicObject(string objectId, THostObjectType obj) => _flatAtomicObjects[objectId] = obj;

  public void AddAtomicDefinitionObject(string objectId, THostObjectType obj) =>
    _flatAtomicDefinitionObjects[objectId] = obj;

  public void AddInstanceProxiesByDefinitionId(string definitionId, List<InstanceProxy> instanceProxies) =>
    _instanceProxiesByDefinitionId[definitionId] = instanceProxies;

  public UnpackResult<THostObjectType> GetUnpackResult() =>
    new(GetAtomicObjects(), GetAtomicDefinitionObjects(), GetInstanceProxies(), GetDefinitionProxies());

  public bool TryGetInstanceProxiesFromDefinitionId(
    string definitionId,
    [NotNullWhen(true)] out List<InstanceProxy>? instanceProxiesWithSameDefinition
  )
  {
    if (_instanceProxiesByDefinitionId.TryGetValue(definitionId, out List<InstanceProxy>? value))
    {
      instanceProxiesWithSameDefinition = value;
      return true;
    }
    instanceProxiesWithSameDefinition = null;
    return false;
  }

  public bool TryGetInstanceDefinitionProxy(
    string definitionId,
    [NotNullWhen(true)] out InstanceDefinitionProxy? instanceDefinitionProxy
  )
  {
    if (_definitionProxies.TryGetValue(definitionId, out InstanceDefinitionProxy? value))
    {
      instanceDefinitionProxy = value;
      return true;
    }

    instanceDefinitionProxy = null;
    return false;
  }

  public InstanceProxy GetInstanceProxy(string instanceId) => _instanceProxies[instanceId];

  private List<THostObjectType> GetAtomicObjects() => _flatAtomicObjects.Values.ToList();

  private List<THostObjectType> GetAtomicDefinitionObjects() => _flatAtomicDefinitionObjects.Values.ToList();

  private List<InstanceDefinitionProxy> GetDefinitionProxies() => _definitionProxies.Values.ToList();

  private Dictionary<string, InstanceProxy> GetInstanceProxies() => _instanceProxies;

  /// <summary>
  /// Update children max depths whenever definition proxy is found on the unpacked dictionary (<see cref="_definitionProxies"/>).
  /// Even if definition unpacked before, max depth of its children must be updated if upcoming max depth is higher than existing one.
  /// </summary>
  /// <param name="definitionProxy"> Definition proxy to update max depth of its children.</param>
  /// <param name="depthDifference"> Value to increase max depth of children.</param>
  public void UpdateChildrenMaxDepth(InstanceDefinitionProxy definitionProxy, int depthDifference)
  {
    // Increase depth of definition
    definitionProxy.maxDepth += depthDifference;

    // Find instance proxies of given definition
    var definitionInstanceProxies = definitionProxy
      .objects.Where(id => _instanceProxies.TryGetValue(id, out _))
      .Select(id => _instanceProxies[id])
      .ToList();

    // Break the loop if no instance proxy found under definition.
    if (definitionInstanceProxies.Count == 0)
    {
      return;
    }

    var subDefinitions = new Dictionary<string, InstanceDefinitionProxy>();
    foreach (InstanceProxy instanceProxy in definitionInstanceProxies)
    {
      // Increase depth of instance
      instanceProxy.maxDepth += depthDifference;
      // Collect sub definitions
      subDefinitions[instanceProxy.definitionId] = _definitionProxies[instanceProxy.definitionId];
    }

    // Iterate through sub definitions
    foreach (var subDefinition in subDefinitions.Values)
    {
      UpdateChildrenMaxDepth(subDefinition, depthDifference);
    }
  }
}
