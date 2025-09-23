using Speckle.Connectors.CSiShared.HostApp.Helpers;
using Speckle.Connectors.ETABSShared.HostApp.Services;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.ETABSShared.HostApp.Helpers;

/// <summary>
/// Extracts ETABS-specific frame section properties.
/// </summary>
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
  /// Gets frame section properties
  /// </summary>
  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties)
  {
    // get pre-loaded frame definitions from the service
    if (!_definitionService.FrameDefinitions.TryGetValue(sectionName, out var rawDatabaseTableProperties))
    {
      return; // No definitions found for this section
    }

    // define table keys that we don't want to exclude in the section proxy properties
    var keysToExclude = new HashSet<string>
    {
      "GUID",
      "Name",
      "Color",
      "Notes",
      "FileName",
      "FromFile",
      "SectInFile",
      "NotAutoFact"
    };

    // get the shape of the section using the dedicated api query (exception to the database approach)
    eFramePropType framePropType = 0;
    _settingsStore.Current.SapModel.PropFrame.GetTypeOAPI(sectionName, ref framePropType);
    Dictionary<string, object?> generalProperties = properties.EnsureNested(SectionPropertyCategory.GENERAL_DATA);
    generalProperties.Add("Section Shape", framePropType.ToString());

    // NOTE: this is gross and quite dangerous 🤨 but beats specific frame prop sect. property extractions imo
    foreach (KeyValuePair<string, string> rawDatabaseTableProperty in rawDatabaseTableProperties)
    {
      string key = rawDatabaseTableProperty.Key;
      string value = rawDatabaseTableProperty.Value;

      if (!keysToExclude.Contains(key))
      {
        if (key == "Material")
        {
          generalProperties.Add(key, value);
        }
        else if (key.EndsWith("Mod"))
        {
          Dictionary<string, object?> modificationProperties = properties.EnsureNested(
            SectionPropertyCategory.MODIFIERS
          );
          modificationProperties.Add(key, double.Parse(value));
        }
        else
        {
          // dangerously assuming -> if we get to this stage and string can be parsed as a double, it's section dimension
          if (double.TryParse(value, out double parsedValue))
          {
            Dictionary<string, object?> sectionDimensions = properties.EnsureNested(
              SectionPropertyCategory.SECTION_DIMENSIONS
            );
            sectionDimensions.Add(key, parsedValue);
          }
        }
      }
    }
  }
}
