using Speckle.InterfaceGenerator;

namespace Speckle.Converter.Navisworks.Services;

[GenerateAutoInterface]
public class ModelItemPropertySetsCache(IElementSelectionService elementSelectionService) : IModelItemPropertySetsCache
{
  private readonly Dictionary<string, Dictionary<string, object?>> _cache = new(StringComparer.Ordinal);

  public int CacheHits { get; private set; }

  public int CacheMisses { get; private set; }

  public bool TryGet(NAV.ModelItem modelItem, out Dictionary<string, object?> propertySets)
  {
    var path = elementSelectionService.GetModelItemPath(modelItem);
    if (_cache.TryGetValue(path, out var cached))
    {
      CacheHits++;
      propertySets = cached;
      return true;
    }

    propertySets = null!;
    return false;
  }

  public void Store(NAV.ModelItem modelItem, Dictionary<string, object?> propertySets)
  {
    var path = elementSelectionService.GetModelItemPath(modelItem);
    if (!_cache.ContainsKey(path))
    {
      CacheMisses++;
    }

    _cache[path] = propertySets;
  }

  public void Reset()
  {
    _cache.Clear();
    CacheHits = 0;
    CacheMisses = 0;
  }

  public ModelItemPropertySetsCacheSnapshot Snapshot() => new(_cache.Count, CacheHits, CacheMisses);
}

public readonly record struct ModelItemPropertySetsCacheSnapshot(int CachedNodeCount, int CacheHits, int CacheMisses);
