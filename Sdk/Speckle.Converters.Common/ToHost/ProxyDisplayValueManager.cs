using Speckle.Objects.Geometry;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.Common.ToHost;

public class ProxyDisplayValueManager : IProxyDisplayValueManager
{
  // definitionId → list of definition meshes. This map holds the whole truth of the instance geometry.
  private readonly Dictionary<string, List<Mesh>> _definitionGeometry = [];

  /// <summary>
  /// The Unpacker's job. This initializes the cache by finding and storing all the definition geometries.
  /// </summary>
  /// <remarks>
  /// We call this once, right after deserialization.
  /// </remarks>
  public void Initialize(
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies,
    IReadOnlyCollection<TraversalContext> allObjects
  )
  {
    if (definitionProxies == null || definitionProxies.Count == 0)
    {
      return; // no instances, nothing to see here.
    }

    var definitionObjectIds = new HashSet<string>(definitionProxies.SelectMany(dp => dp.objects));

    foreach (var tc in allObjects)
    {
      if (tc.Current.applicationId != null && definitionObjectIds.Contains(tc.Current.applicationId))
      {
        if (tc.Current is not Mesh mesh)
        {
          // constrained to only deal with meshes here for now!
          // TODO: extend for other geometry types which will eventually come, maybe now even (ODA?)
          throw new NotSupportedException("Only proxified mesh display values currently supported");
        }

        var defProxy = definitionProxies.First(dp => dp.objects.Contains(tc.Current.applicationId));

        if (!_definitionGeometry.TryGetValue(defProxy.applicationId!, out var meshList))
        {
          _definitionGeometry[defProxy.applicationId!] = meshList = new List<Mesh>();
        }

        meshList.Add(mesh);
      }
    }
  }

  /// <inheritdoc />
  public IReadOnlyList<Mesh> ResolveInstanceProxy(InstanceProxy proxy)
  {
    if (!_definitionGeometry.TryGetValue(proxy.definitionId, out var definitionMeshes))
    {
      // if definition is missing, we return an empty list and let the fallback handle it.
      return [];
    }

    return definitionMeshes;
  }
}
