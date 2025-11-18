using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
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
    var sectionProps = GetSectionProperties(sectionName);

    // shallow copy nested dictionaries into provided properties dict to mutate it (required by interface contract)
    foreach (var kvp in sectionProps)
    {
      properties[kvp.Key] = kvp.Value;
    }
  }

  private Dictionary<string, object?> GetSectionProperties(string sectionName)
  {
    // return cached properties directly
    if (_csiToSpeckleCacheSingleton.ShellSectionPropertiesCache.TryGetValue(sectionName, out var cachedProperties))
    {
      return cachedProperties;
    }

    // fallback - shouldn't happen because cached populated on the fly as sections appear in the extractor
    _logger.LogWarning(
      "Section {SectionName} not in cache during unpacking - resolving via API (expensive)",
      sectionName
    );

    var resolved = _etabsShellSectionResolver.ResolveSection(sectionName);
    _csiToSpeckleCacheSingleton.ShellSectionPropertiesCache[sectionName] = resolved;
    return resolved;
  }
}
