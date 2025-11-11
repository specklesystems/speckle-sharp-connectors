using Speckle.Objects.Geometry;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.Common.ToHost;

/// <summary>
/// Interface that defines how the converter gets its pre-resolved geometry (from a proxy to a geometry).
/// </summary>
public interface IProxyDisplayValueManager
{
  void Initialize(
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies,
    IReadOnlyCollection<TraversalContext> allObjects
  );

  /// <summary>
  /// Asks the cache to hand over the fully transformed meshes for a given instance proxy.
  /// </summary>
  IReadOnlyList<Mesh> ResolveInstanceProxy(InstanceProxy proxy);
}
