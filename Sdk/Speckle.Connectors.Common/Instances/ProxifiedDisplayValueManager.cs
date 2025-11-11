using Speckle.DoubleNumerics;
using Speckle.Objects;
using Speckle.Objects.Geometry;
using Speckle.Objects.Other;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

/// <summary>
/// Manages access to instance definition geometry for resolving InstanceProxy objects in DataObject displayValues.
/// </summary>
/// <remarks>
/// Assumption that all definitions are used (and needed). i.e. loads everything.
/// </remarks>
public class ProxifiedDisplayValueManager
{
  // definitionId → list of definition meshes
  private readonly Dictionary<string, List<Mesh>> _definitionGeometry = new();

  /// <summary>
  /// Initialize by finding all definition geometries in a single pass.
  /// </summary>
  /// <remarks>
  /// Call this after unpacking the root object, before converting. Order matters (sucks, I know!).
  /// </remarks>
  public void Initialize(
    IReadOnlyCollection<InstanceDefinitionProxy>? definitionProxies,
    IReadOnlyCollection<TraversalContext> allObjects
  )
  {
    if (definitionProxies == null || definitionProxies.Count == 0)
    {
      return; // no instances in this model, nothing to do
    }

    // build a set of all object IDs that are part of instance definitions (to get us O(1) lookup when searching)
    var definitionObjectIds = new HashSet<string>(definitionProxies.SelectMany(dp => dp.objects));

    // single pass through all objects - find the ones that are definition meshes
    foreach (var tc in allObjects)
    {
      if (tc.Current.applicationId != null && definitionObjectIds.Contains(tc.Current.applicationId))
      {
        // under the assumption that we only proxifying meshes, if we encounter non-mesh, we should throw?
        if (tc.Current is not Mesh mesh)
        {
          throw new InvalidOperationException("Proxified display values should only contain Mesh geometry");
        }

        // this mesh is part of an instance definition, find which definition proxy it belongs to
        var defProxy = definitionProxies.First(dp => dp.objects.Contains(tc.Current.applicationId));

        // store in list - a definition can have multiple meshes
        if (!_definitionGeometry.TryGetValue(defProxy.applicationId!, out var meshList))
        {
          _definitionGeometry[defProxy.applicationId!] = meshList = new List<Mesh>();
        }

        meshList.Add(mesh);
      }
    }
  }

  /// <summary>
  /// Resolve an InstanceProxy to its transformed meshes, ready for conversion.
  /// </summary>
  /// <remarks>
  /// Applies the instance transform to each definition mesh.
  /// </remarks>
  public IReadOnlyList<Mesh> ResolveInstanceProxy(InstanceProxy proxy)
  {
    // get definition meshes
    if (!_definitionGeometry.TryGetValue(proxy.definitionId, out var definitionMeshes))
    {
      // definition not found - shouldn't happen if data is clean
      return [];
    }

    var transformedMeshes = new List<Mesh>(definitionMeshes.Count);

    // apply instance transform to each definition mesh
    foreach (var defMesh in definitionMeshes)
    {
      var transformed = ApplyTransform(defMesh, proxy.transform, proxy.units);
      transformedMeshes.Add(transformed);
    }

    return transformedMeshes;
  }

  /// <summary>
  /// Apply a transform to a mesh, cloning to avoid mutating the cached definition.
  /// </summary>
  private static Mesh ApplyTransform(Mesh mesh, Matrix4x4 transform, string units)
  {
    // shallow copy to avoid mutating the cached definition mesh
    var copiedMesh = (Mesh)mesh.ShallowCopy();

    // apply transform
    var speckleTransform = new Transform { matrix = transform, units = units };

    copiedMesh.TransformTo(speckleTransform, out ITransformable result);

    return (Mesh)result;
  }

  public void Clear() => _definitionGeometry.Clear();
}
