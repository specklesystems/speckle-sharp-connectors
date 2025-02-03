using Speckle.Objects.Other;
using Speckle.Sdk.Models.Proxies;

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

  /// <summary>
  /// map(materialId, proxy)
  /// </summary>
  /// <remarks>
  /// TODO: GroupProxy misuse? Formalize together with Etabs (claire and bjorn)
  /// </remarks>
  public Dictionary<string, GroupProxy> MaterialProxiesMap { get; } = new();

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
  /// Returns the material proxies for the given element IDs.
  /// </summary>
  /// <param name="elementIds">List of element IDs to get material proxies for</param>
  /// <returns>List of material proxies that are associated with the given elements</returns>
  public List<GroupProxy> GetMaterialProxyListForObjects(List<string> elementIds) =>
    MaterialProxiesMap.Values.Where(proxy => proxy.objects.Any(elementIds.Contains)).ToList();
}
