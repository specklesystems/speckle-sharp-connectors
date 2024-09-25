using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// <para>
/// Lifetime of this singleton should be per document.
/// </para>
/// <para>
/// Persistent cache (across conversions) for all generated render material proxies.
/// This cache stores a list of render material proxies per element id, and provides a method to get out the merged render material proxy list from a set of object ids for setting on the root commit object post conversion phase.
/// </para>
/// <para>
/// Why is this needed? Because two reasons: send caching bypasses converter and revit conversions typically generate multiple display values per element.
/// Ask dim for more and he might start crying.
/// </para>
/// TODO: this dude needs to be split into single responsability (render materials and material quantities), and removed from the context - as it's not needed for it to be there. It can be DI'ed as appropriate (see ParameterDefinitionHandler)
/// </summary>
public class RevitToSpeckleCacheSingleton
{
  /// <summary>
  /// (DB.Material id, RevitMaterial). This can be generated from converting render materials or material quantities.
  /// </summary>
  public Dictionary<string, RevitMaterial> RevitMaterialCache { get; } = new();

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
