using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

public record UnpackResult<T>(
  List<T> AtomicObjects,
  HashSet<string> AtomicDefinitionObjectIds,
  Dictionary<string, InstanceProxy> InstanceProxies,
  List<InstanceDefinitionProxy> InstanceDefinitionProxies
);
