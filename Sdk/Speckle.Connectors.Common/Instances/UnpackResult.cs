using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

public record UnpackResult<T>(
  List<T> AtomicObjects,
  List<T> AtomicDefinitionObjects,
  Dictionary<string, InstanceProxy> InstanceProxies,
  List<InstanceDefinitionProxy> InstanceDefinitionProxies
);
