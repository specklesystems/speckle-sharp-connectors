using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

namespace Speckle.Connectors.ETABSShared.HostApp.Helpers;

/// <summary>
/// Extracts ETABS-specific shell section properties.
/// </summary>
public class EtabsShellSectionPropertyExtractor : IApplicationShellSectionPropertyExtractor
{
  private readonly ILogger<EtabsShellSectionPropertyExtractor> _logger;
  private readonly CsiToSpeckleCacheSingleton _csiToSpeckleCacheSingleton;
  private readonly EtabsShellSectionResolver _etabsShellSectionResolver;

  public EtabsShellSectionPropertyExtractor(
    ILogger<EtabsShellSectionPropertyExtractor> logger,
    EtabsShellSectionResolver etabsShellSectionResolver,
    CsiToSpeckleCacheSingleton csiToSpeckleCacheSingleton
  )
  {
    _logger = logger;
    _etabsShellSectionResolver = etabsShellSectionResolver;
    _csiToSpeckleCacheSingleton = csiToSpeckleCacheSingleton;
  }

  /// <summary>
  /// Extract shell section properties from cache.
  /// </summary>
  /// <remarks>
  /// By the time this method is called during section unpacking, all sections should already be
  /// resolved and cached by <see cref="EtabsShellPropertiesExtractor"/> during object conversion.
  /// </remarks>
  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties)
  {
    // read from cache (populated during conversion)
    if (_csiToSpeckleCacheSingleton.ShellSectionPropertiesCache.TryGetValue(sectionName, out var cachedProperties))
    {
      CopyCachedProperties(cachedProperties, properties);
      return;
    }

    // fallback - section not in cache (shouldn't happen for sections in ShellSectionCache), but ... who knows? Etabs...
    _logger.LogWarning("Section {SectionName} not found in cache during unpacking", sectionName);

    Dictionary<string, object?> resolvedProperties = _etabsShellSectionResolver.ResolveSection(sectionName);

    // cache it for next time (shouldn't be needed but defensive)
    _csiToSpeckleCacheSingleton.ShellSectionPropertiesCache[sectionName] = resolvedProperties;

    CopyCachedProperties(resolvedProperties, properties);
  }

  private void CopyCachedProperties(Dictionary<string, object?> source, Dictionary<string, object?> destination)
  {
    foreach (var kvp in source)
    {
      if (kvp.Value is not Dictionary<string, object?> nestedValues)
      {
        _logger.LogWarning(
          "Unexpected value type for key {Key}, expected Dictionary<string, object?>, got {ActualType}",
          kvp.Key,
          kvp.Value?.GetType().Name ?? "null"
        );
        continue;
      }

      var nestedProperties = destination.EnsureNested(kvp.Key);
      foreach (var nested in nestedValues)
      {
        nestedProperties[nested.Key] = nested.Value;
      }
    }
  }
}
