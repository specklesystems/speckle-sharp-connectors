using Speckle.Objects.Other;
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
public class RevitToSpeckleCacheSingleton
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
  /// Returns the merged material proxy list for the given object ids. Use this to get post conversion a correct list of material proxies for setting on the root commit object.
  /// </summary>
  /// <param name="elementIds"></param>
  /// <returns></returns>
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
          value = kvp.Value;
          mergeTarget[kvp.Key] = value;
          continue;
        }
        value.objects.AddRange(kvp.Value.objects);
      }
    }
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

  public void ClearCache()
  {
    SpeckleRenderMaterialCache.Clear();
    InstanceDefinitionProxiesMap.Clear();
    InstancedObjects.Clear();
  }
}
