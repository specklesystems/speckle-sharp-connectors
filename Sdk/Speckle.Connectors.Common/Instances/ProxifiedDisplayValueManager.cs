using Speckle.Sdk.Models;
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
  // definitionId → actual mesh geometry
  private readonly Dictionary<string, List<Base>> _definitionGeometry = new();

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

    // build a set of all object IDs that are part of instance definitions (to get us a O(1) lookup when searching)
    var definitionObjectIds = new HashSet<string>(definitionProxies.SelectMany(dp => dp.objects));

    // single pass through all objects - find the ones that are definition geometries
    foreach (var tc in allObjects)
    {
      if (tc.Current.applicationId != null && definitionObjectIds.Contains(tc.Current.applicationId))
      {
        // this object is part of an instance definition, find which definition proxy it belongs to
        var defProxy = definitionProxies.First(dp => dp.objects.Contains(tc.Current.applicationId));

        // store in list - a definition can have multiple geometry objects
        if (!_definitionGeometry.TryGetValue(defProxy.applicationId!, out var geometryList))
        {
          _definitionGeometry[defProxy.applicationId!] = geometryList = new List<Base>();
        }

        geometryList.Add(tc.Current);
      }
    }
  }

  /// <summary>
  /// Get the definition geometries for an InstanceProxy.
  /// </summary>
  /// <returns>Returns all geometry objects that make up this definition</returns>
  public IReadOnlyList<Base>? GetDefinitionGeometry(string definitionId) =>
    _definitionGeometry.TryGetValue(definitionId, out var geometry) ? geometry : null;

  public void Clear() => _definitionGeometry.Clear();
}
