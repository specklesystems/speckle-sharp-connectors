using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.ETABSShared.HostApp.Helpers;

/// <summary>
/// Extracts ETABS-specific shell section properties.
/// </summary>
public class EtabsShellSectionPropertyExtractor : IApplicationShellSectionPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly ILogger<EtabsShellSectionPropertyExtractor> _logger;
  private readonly EtabsShellSectionResolver _etabsShellSectionResolver;

  public EtabsShellSectionPropertyExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ILogger<EtabsShellSectionPropertyExtractor> logger,
    EtabsShellSectionResolver etabsShellSectionResolver
  )
  {
    _settingsStore = settingsStore;
    _logger = logger;
    _etabsShellSectionResolver = etabsShellSectionResolver;
  }

  /// <summary>
  /// Extract shell section properties
  /// </summary>
  /// <remarks>
  /// sectionName is unique across all types (Wall, Slab and Deck)
  /// There is no general query such as PropArea.GetShell() - rather we have to be specific on the type, for example
  /// PropArea.GetWall() or PropArea.GetDeck() BUT we can't get the building type given a SectionName.
  /// Hence the introduction of ResolveSection.
  /// </remarks>
  public void ExtractProperties(string sectionName, SectionPropertyExtractionResult dataExtractionResult)
  {
    // Step 01: Finding the appropriate api query for the unknown section type (wall, deck or slab)
    (string materialName, Dictionary<string, object?> resolvedProperties) = _etabsShellSectionResolver.ResolveSection(
      sectionName
    );

    // Step 02: Assign found material to extraction result
    dataExtractionResult.MaterialName = materialName;

    // Step 03: Mutate properties dictionary with resolved properties
    foreach (var nestedDictionary in resolvedProperties)
    {
      if (nestedDictionary.Value is not Dictionary<string, object?> nestedValues)
      {
        _logger.LogWarning(
          "Unexpected value type for key {Key} in section {SectionName}. Expected Dictionary<string, object?>, got {ActualType}",
          nestedDictionary.Key,
          sectionName,
          nestedDictionary.Value?.GetType().Name ?? "null"
        );
        continue;
      }

      var nestedProperties = DictionaryUtils.EnsureNestedDictionary(
        dataExtractionResult.Properties,
        nestedDictionary.Key
      );
      foreach (var kvp in nestedValues)
      {
        nestedProperties[kvp.Key] = kvp.Value;
      }
    }
  }
}
