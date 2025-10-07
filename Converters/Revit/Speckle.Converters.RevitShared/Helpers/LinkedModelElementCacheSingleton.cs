using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Caches converted elements from linked models to avoid redundant conversions when the same linked model is instanced
/// multiple times. Scoped per send operation.
/// </summary>
public sealed class LinkedModelElementCacheSingleton
{
  private readonly Dictionary<string, Base> _cache = [];

  // TODO: delete - these are just for dev logging
  public int CacheMisses { get; private set; }
  public int CacheHits { get; private set; }
  public double CacheHitRate => (CacheHits + CacheMisses) == 0 ? 0 : (CacheHits * 100) / (CacheHits + CacheMisses);

  /// <summary>
  /// Attempts to retrieve a cached element from a linked model.
  /// </summary>
  public bool TryGetCachedElement(string documentPath, string elementUniqueId, out Base? cachedElement)
  {
    string key = CreateCacheKey(documentPath, elementUniqueId);

    if (_cache.TryGetValue(key, out cachedElement))
    {
      CacheHits++;
      return true;
    }

    CacheMisses++;
    cachedElement = null;
    return false;
  }

  /// <summary>
  /// Stores a converted element in the cache.
  /// </summary>
  public void StoreCachedElement(string documentPath, string elementUniqueId, Base convertedElement)
  {
    string key = CreateCacheKey(documentPath, elementUniqueId);
    _cache[key] = convertedElement;
  }

  public void Clear() => _cache.Clear();

  /// <summary>
  /// Creates a unique cache key by combining document path and element ID.
  /// </summary>
  /// <remarks>
  /// Defensively adding document path as key suffix for the (unlikely) occurence of same element ID across different models.
  /// </remarks>
  private static string CreateCacheKey(string documentPath, string elementUniqueId) =>
    $"{documentPath}_{elementUniqueId}";
}
