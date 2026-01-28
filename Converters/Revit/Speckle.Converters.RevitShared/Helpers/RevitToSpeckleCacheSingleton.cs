using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.ToSpeckle;
using Speckle.Objects.Other;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// <para>
/// Lifetime of this singleton should be per document.
/// </para>
/// <para>
/// Why is this needed? Because two reasons: send caching bypasses converter and revit conversions typically generate multiple display values per element.
/// Ask dim for more and he might start crying.
/// </para>
/// </summary>
public class RevitToSpeckleCacheSingleton(ILogger<RevitToSpeckleCacheSingleton> logger)
{
  /// <summary>
  /// (DB.Material id, RenderMaterial). This can be generated from converting render materials or material quantities.
  /// </summary>
  public Dictionary<string, RenderMaterial> SpeckleRenderMaterialCache { get; } = new();

  /// <summary>
  /// map(object id, ( map (materialId, proxy) ) )
  /// a per object map of material proxies. not the best way???
  /// </summary>
  public Dictionary<string, Dictionary<string, RenderMaterialProxy>> ObjectRenderMaterialProxiesMap { get; } = new();

  public Dictionary<
    string,
    (List<string> elementIds, InstanceDefinitionProxy definitionProxy)
  > InstanceDefinitionProxiesMap { get; } = new();

  public Dictionary<string, (List<string> elementIds, Base baseObj)> InstancedObjects { get; } = new();

  /// <summary>
  /// Maps mesh application IDs to their material IDs for later proxy population.
  /// Dictionary: elementId -> (meshAppId -> materialId)
  /// </summary>
  public Dictionary<string, Dictionary<string, string>> MeshToMaterialMap { get; } = new();

  /// <summary>
  /// Returns the merged material proxy list for the given object IDs.
  /// Use this post-conversion to get a correct list of material proxies for the root commit object.
  /// </summary>
  /// <returns>A deduplicated list of <see cref="RenderMaterialProxy"/> objects for all specified elements.</returns>
  /// <remarks>
  /// <para>
  /// Material proxy objects lists should already be correctly populated at this point (with definition mesh IDs for instances
  /// and individual mesh IDs for non-instances), so the merging primarily handles cross-element scenarios rather than
  /// fixing incorrect data.
  /// </para>
  /// </remarks>
  public List<RenderMaterialProxy> GetRenderMaterialProxyListForObjects(List<string> elementIds)
  {
    var proxiesToMerge = ObjectRenderMaterialProxiesMap
      .Where(kvp => elementIds.Contains(kvp.Key))
      .Select(kvp => kvp.Value);

    var mergeTarget = new Dictionary<string, RenderMaterialProxy>();
    foreach (var dictionary in proxiesToMerge)
    {
      foreach (var kvp in dictionary)
      {
        if (!mergeTarget.TryGetValue(kvp.Key, out RenderMaterialProxy? value))
        {
          // first time seeing this material - add it
          mergeTarget[kvp.Key] = kvp.Value;
        }
        else
        {
          // merge objects lists (should already be mostly correct now)
          value.objects.AddRange(kvp.Value.objects);
        }
      }
    }

    // final deduplication (should be minimal now)
    foreach (var renderMaterialProxy in mergeTarget.Values)
    {
      renderMaterialProxy.objects = renderMaterialProxy.objects.Distinct().ToList();
    }

    return mergeTarget.Values.ToList();
  }

  /// <summary>
  /// Gets instance definition proxies from session cache for the given element ids.
  /// This is necessary because send caching only check against DB.Element since it is the managed object in Revit UI.
  /// We need to filter already existant definition proxies from cache with their element id relationship.
  /// Otherwise, we will end up with incomplete data in root.
  /// </summary>
  /// <param name="elementIds">Ids to get corresponding definition proxies that cached before.</param>
  public List<InstanceDefinitionProxy> GetInstanceDefinitionProxiesForObjects(List<string> elementIds) =>
    InstanceDefinitionProxiesMap
      .Values.Where(v => v.elementIds.Any(id => elementIds.Contains(id)))
      .Select(v => v.definitionProxy)
      .ToList();

  /// <summary>
  /// Gets atomic objects (Base) that extracted out from display value of RevitDataObject.
  /// We need to filter already existant atomic objects from cache with their element id relationship.
  /// Otherwise, we will end up with incomplete data in root.
  /// </summary>
  /// <param name="elementIds">Element ids to get corresponding atomic objects (Base) that cached before.</param>
  /// <returns></returns>
  public List<Base> GetBaseObjectsForObjects(List<string> elementIds) =>
    InstancedObjects.Values.Where(v => v.elementIds.Any(id => elementIds.Contains(id))).Select(v => v.baseObj).ToList();

  /// <summary>
  /// Adds a mesh ID to the appropriate material proxy.
  /// For instances: adds the definition mesh ID.
  /// For non-instances: adds the mesh's own ID.
  /// </summary>
  /// <remarks>
  /// Cache navigation logic is encapsulated here. Failures are logged but do not throw exceptions,
  /// allowing conversion to continue even if material assignment fails.
  /// </remarks>
  public void AddMeshToMaterialProxy(string elementId, SOG.Mesh mesh, bool isInstance)
  {
    // get material proxy map
    if (!ObjectRenderMaterialProxiesMap.TryGetValue(elementId, out var proxyMap))
    {
      logger.LogError("Cache inconsistency: Material proxy map not found for element {ElementId}", elementId);
      return;
    }

    var materialId = GetMaterialId(elementId, mesh);

    // get specific material proxy
    if (!proxyMap.TryGetValue(materialId, out var materialProxy))
    {
      if (materialId != ElementId.InvalidElementId.ToString())
      {
        logger.LogError(
          "Cache inconsistency: Material proxy not found for material {MaterialId} in element {ElementId}",
          materialId,
          elementId
        );
      }

      return;
    }

    // determine which mesh ID to add
    string meshIdToAdd;

    if (isInstance)
    {
      var instanceDefinitionId = MeshInstanceIdGenerator.GenerateUntransformedMeshId(mesh);
      instanceDefinitionId += materialId;

      if (!InstancedObjects.TryGetValue(instanceDefinitionId, out var instancedObject))
      {
        throw new InvalidOperationException(
          $"Instance definition '{instanceDefinitionId}' not found in cache for mesh '{mesh.applicationId}'"
        );
      }

      meshIdToAdd = instancedObject.baseObj.applicationId.NotNull();
    }
    else
    {
      meshIdToAdd = mesh.applicationId.NotNull();
    }

    // add to proxy if not already present
    if (!materialProxy.objects.Contains(meshIdToAdd))
    {
      materialProxy.objects.Add(meshIdToAdd);
    }
  }

  public string GetMaterialId(string elementId, SOG.Mesh mesh)
  {
    // get mesh-to-material mapping
    if (!MeshToMaterialMap.TryGetValue(elementId, out var meshMatMap))
    {
      logger.LogWarning("No mesh-to-material mapping found for element {ElementId}", elementId);
      return ElementId.InvalidElementId.ToString();
    }

    // get material ID for this mesh
    if (!meshMatMap.TryGetValue(mesh.applicationId.NotNull(), out var materialId))
    {
      logger.LogError(
        "Cache inconsistency: Mesh {MeshId} not found in material mapping for element {ElementId}",
        mesh.applicationId,
        elementId
      );
      return ElementId.InvalidElementId.ToString();
    }

    return materialId;
  }

  public void ClearCache()
  {
    ObjectRenderMaterialProxiesMap.Clear();
    SpeckleRenderMaterialCache.Clear();
    InstanceDefinitionProxiesMap.Clear();
    InstancedObjects.Clear();
    MeshToMaterialMap.Clear();
  }
}
