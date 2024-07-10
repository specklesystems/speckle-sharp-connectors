using Speckle.Core.Models.Instances;

namespace Speckle.Connectors.Utils.Instances;

public record UnpackResult<T>(
  List<T> AtomicObjects,
  Dictionary<string, InstanceProxy> InstanceProxies,
  List<InstanceDefinitionProxy> InstanceDefinitionProxies
);
