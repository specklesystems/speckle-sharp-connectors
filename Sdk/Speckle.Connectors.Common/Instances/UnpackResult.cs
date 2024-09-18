using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

public record UnpackResult<T>(
  List<T> AtomicObjects,
  Dictionary<string, InstanceProxy> InstanceProxies,
  List<InstanceDefinitionProxy> InstanceDefinitionProxies
);
