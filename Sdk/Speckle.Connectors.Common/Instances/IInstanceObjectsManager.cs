using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

public interface IInstanceObjectsManager<THostObjectType, TAppIdMapValueType>
{
  void AddInstanceProxy(string objectId, InstanceProxy instanceProxy);
  void AddDefinitionProxy(string objectId, InstanceDefinitionProxy instanceDefinitionProxy);
  void AddAtomicObject(string objectId, THostObjectType obj);
  void AddAtomicDefinitionObject(string objectId, THostObjectType obj);
  void AddInstanceProxiesByDefinitionId(string definitionId, List<InstanceProxy> instanceProxies);
  UnpackResult<THostObjectType> GetUnpackResult();
  bool TryGetInstanceProxiesFromDefinitionId(
    string definitionId,
    [NotNullWhen(true)] out List<InstanceProxy>? instanceProxiesWithSameDefinition
  );
  bool TryGetInstanceDefinitionProxy(
    string definitionId,
    [NotNullWhen(true)] out InstanceDefinitionProxy? instanceDefinitionProxy
  );
  InstanceProxy GetInstanceProxy(string instanceId);

  void UpdateChildrenMaxDepth(InstanceDefinitionProxy definitionProxy, int depthDifference);
}
