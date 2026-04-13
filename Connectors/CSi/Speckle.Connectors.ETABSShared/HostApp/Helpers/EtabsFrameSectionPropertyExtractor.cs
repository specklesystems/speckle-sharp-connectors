using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Services;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.ETABSShared.HostApp.Helpers;

/// <summary>
/// Extracts ETABS-specific frame section properties.
/// </summary>
/// <remarks>
/// The bulk loading strategy is necessary here because we can't know which database table contains which section
/// beforehand - there are multiple tables like "Frame Section Property Definitions - Steel",
/// "Frame Section Property Definitions - Concrete", etc.
/// </remarks>
public class EtabsFrameSectionPropertyExtractor : IApplicationFrameSectionPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;
  private readonly EtabsSectionPropertyDefinitionService _definitionService;

  public EtabsFrameSectionPropertyExtractor(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    EtabsSectionPropertyDefinitionService definitionService
  )
  {
    _settingsStore = settingsStore;
    _definitionService = definitionService;
  }

  /// <summary>
  /// Gets frame section properties from preloaded database table data
  /// </summary>
  /// <remarks>
  /// Property categorization is done heuristically - order matters in the parsing logic.
  /// </remarks>
  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties)
  {
    // get frame definitions from the service (which uses database table extraction)
    // this is a fast dictionary lookup since all data is preloaded
    if (!_definitionService.FrameDefinitions.TryGetValue(sectionName, out var rawDatabaseTableProperties))
    {
      return; // no definitions found for this section
    }

    // define table keys that we don't want to include in the section proxy properties
    var keysToExclude = new HashSet<string>
    {
      "GUID",
      "Name",
      "Color",
      "Notes",
      "FileName",
      "FromFile",
      "SectInFile",
      "NotAutoFact",
    };

    // get the section type / shape using the dedicated api query (exception to the database approach)
    // this specific property isn't available in the database table extraction
    eFramePropType framePropType = 0;
    _settingsStore.Current.SapModel.PropFrame.GetTypeOAPI(sectionName, ref framePropType);
    Dictionary<string, object?> generalProperties = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
    generalProperties.Add("Section Shape", framePropType.ToString());

    // heuristic property categorization based on key patterns and parse-ability
    // NOTE: this is gross and quite dangerous 🤨 but beats specific frame prop sect. property extractions imo
    // order matters here! we check for known string props first, then modifiers, then assume doubles are dimensions
    foreach (KeyValuePair<string, string> rawDatabaseTableProperty in rawDatabaseTableProperties)
    {
      string key = rawDatabaseTableProperty.Key;
      string value = rawDatabaseTableProperty.Value;

      // skip metadata fields we don't care about
      if (!keysToExclude.Contains(key))
      {
        // material is always a string, grab it first
        if (key == "Material")
        {
          generalProperties.Add(key, value);
        }
        // modifier properties end with "Mod" and should be numeric
        else if (key.EndsWith("Mod") && double.TryParse(value, out double parsedModValue))
        {
          Dictionary<string, object?> modificationProperties = properties.EnsureNested(
            SectionPropertyCategory.MODIFIERS
          );
          modificationProperties.Add(key, parsedModValue);
        }
        // anything else that parses as a double is assumed to be a section dimension
        // this covers things like t3, t2, tf, tw, area, etc. without having to enumerate them all
        else if (double.TryParse(value, out double parsedDimensionValue))
        {
          Dictionary<string, object?> sectionDimensions = properties.EnsureNested(
            SectionPropertyCategory.SECTION_DIMENSIONS
          );
          sectionDimensions.Add(key, parsedDimensionValue);
        }
        // if it doesn't parse as double and isn't a known string property, we skip it
        // this is acceptable - we'd rather miss some edge case properties than crash
      }
    }
  }
}
