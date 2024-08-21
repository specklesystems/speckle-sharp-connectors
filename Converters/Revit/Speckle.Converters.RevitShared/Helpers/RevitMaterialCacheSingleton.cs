using Speckle.Objects.Other.Revit;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// Persistent cache (across conversions) for all generated materials for material quantities.
/// This cache stores converted materials by their ids for faster conversions across all elements.
/// </summary>
public class RevitMaterialCacheSingleton
{
  /// <summary>
  /// (material id, speckle revit material)
  /// </summary>
  public Dictionary<string, RevitMaterial> ConvertedRevitMaterialMap { get; } = new();

  public void AddMaterialToCache(string id, RevitMaterial revitMaterial)
  {
#if NET8_0
    ConvertedRevitMaterialMap.TryAdd(id, revitMaterial);
#else
    if (!ConvertedRevitMaterialMap.ContainsKey(id))
    {
      ConvertedRevitMaterialMap.Add(id, revitMaterial);
    }
#endif
  }
}
