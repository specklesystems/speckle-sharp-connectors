using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Models;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Caches converted elements from linked models to avoid redundant conversions when the same linked model is instanced
/// multiple times.
/// </summary>
/// <remarks>
/// Scoped per send operation. Don't think we can reliably have change tracking on linked model elements, so we clear
/// the cache per operation rather than risk stale data. Also, aim of this cache is to avoid re-converting same elements
/// across multiple instances of same linked model. So, not a cache in the RevitToSpeckleCacheSingleton sense.
/// </remarks>
public sealed class LinkedModelElementCacheScoped
{
  private readonly Dictionary<string, Base> _cache = [];

  // TODO: delete - these are just for dev logging
  public int CacheMisses { get; private set; }
  public int CacheHits { get; private set; }
  public double CacheHitRate => (CacheHits + CacheMisses) == 0 ? 0 : (CacheHits * 100) / (CacheHits + CacheMisses);

  /// <summary>
  /// Attempts to retrieve a cached element from a linked model.
  /// </summary>
  public bool TryGetCachedElement(
    string documentPath,
    string elementUniqueId,
    [NotNullWhen(true)] out Base? cachedElement
  )
  {
    string key = CreateCacheKey(documentPath, elementUniqueId);

    if (_cache.TryGetValue(key, out cachedElement))
    {
      CacheHits++;
      return true;
    }

    CacheMisses++;
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
