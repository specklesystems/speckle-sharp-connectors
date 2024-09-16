using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// <para>Persistent cache (across conversions) for all generated render material proxies. This cache stores a list of render material proxies per element id, and provides a method to get out the merged render material proxy list from a set of object ids for setting on the root commit object post conversion phase.</para>
/// <para>
/// Why is this needed? Because two reasons: send caching bypasses converter and revit conversions typically generate multiple display values per element. Ask dim for more and he might start crying.
/// </para>
/// </summary>
public class RevitMaterialCacheSingleton
{
  /// <summary>
  /// map(object id, ( map (materialId, proxy) ) )
  /// a per object map of material proxies. not the best way???
  /// </summary>
  public Dictionary<string, Dictionary<string, RenderMaterialProxy>> ObjectRenderMaterialProxiesMap { get; } = new();

  public Dictionary<string, string> ObjectIdAndMaterialIndexMap { get; } = new();

  /// <summary>
  /// map (DB.Material id, RevitMaterial). This can be generated from converting render materials or material quantities.
  /// </summary>
  public Dictionary<string, RevitMaterial> ConvertedRevitMaterialMap { get; } = new();

  /// <summary>
  /// map (DB.Material id, RenderMaterial). This can be generated from converting render materials or material quantities.
  /// </summary>
  public Dictionary<string, RenderMaterial> ConvertedRenderMaterialMap { get; } = new();

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
}
