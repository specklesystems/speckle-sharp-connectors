using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Thickness lookup over the resolved wall/slab/deck section properties, shared with the shell property extraction
/// through the section-properties cache so a section is resolved once per send.
/// </summary>
public sealed class EtabsShellThicknessResolver : IShellThicknessResolver
{
  private readonly CsiToSpeckleCacheSingleton _cache;
  private readonly EtabsShellSectionResolver _sectionResolver;

  public EtabsShellThicknessResolver(CsiToSpeckleCacheSingleton cache, EtabsShellSectionResolver sectionResolver)
  {
    _cache = cache;
    _sectionResolver = sectionResolver;
  }

  public double GetThickness(string sectionName)
  {
    if (string.IsNullOrEmpty(sectionName) || sectionName == CsiName.NONE)
    {
      return double.NaN;
    }

    if (!_cache.ShellSectionPropertiesCache.TryGetValue(sectionName, out var sectionProperties))
    {
      sectionProperties = _sectionResolver.ResolveSection(sectionName);
      _cache.ShellSectionPropertiesCache[sectionName] = sectionProperties;
    }

    return ExtractThickness(sectionProperties);
  }

  private static double ExtractThickness(Dictionary<string, object?> sectionProperties)
  {
    if (
      sectionProperties.TryGetValue(SectionPropertyCategory.PROPERTY_DATA, out object? propertyDataObj)
      && propertyDataObj is Dictionary<string, object?> propertyData
      && propertyData.TryGetValue(ObjectPropertyKey.THICKNESS, out object? thicknessObj)
      && thicknessObj is Dictionary<string, object> thicknessDict
      && thicknessDict.TryGetValue("value", out object? valueObj)
      && valueObj is double thickness
    )
    {
      return thickness;
    }

    return double.NaN;
  }
}
