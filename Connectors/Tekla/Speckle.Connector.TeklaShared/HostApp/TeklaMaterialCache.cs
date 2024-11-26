using Speckle.Objects.Other;

namespace Speckle.Connector.Tekla2024.HostApp;

public class TeklaMaterialCache
{
  // cache of color -> RenderMaterial mappings
  public Dictionary<string, RenderMaterial> MaterialCache { get; } = new();

  // cache of object -> material proxy mappings
  public Dictionary<string, Dictionary<string, RenderMaterialProxy>> ObjectProxyMap { get; } = new();

  // clear caches
  public void Clear()
  {
    MaterialCache.Clear();
    ObjectProxyMap.Clear();
  }

  // returns the merged material proxy list for the given object ids
  // use this to get post conversion a correct list of material proxies for setting on the root commit object
  public List<RenderMaterialProxy> GetRenderMaterialProxyListForObjects(List<string> elementIds)
  {
    var proxiesToMerge = ObjectProxyMap
      .Where(valuePair => elementIds.Contains(valuePair.Key))
      .Select(valuePair => valuePair.Value);

    var mergeTarget = new Dictionary<string, RenderMaterialProxy>();
    foreach (var dictionary in proxiesToMerge)
    {
      foreach (var valuePair in dictionary)
      {
        if (!mergeTarget.TryGetValue(valuePair.Key, out RenderMaterialProxy? value))
        {
          value = valuePair.Value;
          mergeTarget[valuePair.Key] = value;
          continue;
        }
        value.objects.AddRange(valuePair.Value.objects);
      }
    }

    foreach (var renderMaterialProxy in mergeTarget.Values)
    {
      renderMaterialProxy.objects = renderMaterialProxy.objects.Distinct().ToList();
    }

    return mergeTarget.Values.ToList();
  }
}
