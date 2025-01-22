using Speckle.Connectors.CSiShared.HostApp.Helpers;
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

  public EtabsFrameSectionPropertyExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Gets generalised frame section properties
  /// </summary>
  /// <remarks>
  /// Sap2000 doesn't support this method, unfortunately
  /// Alternative is to account for extraction according to section type - we're talking over 40 section types!
  /// This way, we get basic information with minimal computational costs.
  /// </remarks>
  public void ExtractProperties(string sectionName, Dictionary<string, object?> properties)
  {
    // Get all frame properties
    int numberOfNames = 0;
    string[] names = [];
    eFramePropType[] propTypes = [];
    double[] t3 = [],
      t2 = [],
      tf = [],
      tw = [],
      t2b = [],
      tfb = [],
      area = [];

    _settingsStore.Current.SapModel.PropFrame.GetAllFrameProperties_2(
      ref numberOfNames,
      ref names,
      ref propTypes,
      ref t3,
      ref t2,
      ref tf,
      ref tw,
      ref t2b,
      ref tfb,
      ref area
    );

    // Find the index of the current section
    int sectionIndex = Array.IndexOf(names, sectionName);

    if (sectionIndex != -1)
    {
      // General Data
      var generalData = DictionaryUtils.EnsureNestedDictionary(properties, SectionPropertyCategory.GENERAL_DATA);
      generalData["type"] = propTypes[sectionIndex].ToString();

      // Section Dimensions
      var sectionDimensions = DictionaryUtils.EnsureNestedDictionary(
        properties,
        SectionPropertyCategory.SECTION_DIMENSIONS
      );
      sectionDimensions["t3"] = t3[sectionIndex];
      sectionDimensions["t2"] = t2[sectionIndex];
      sectionDimensions["tf"] = tf[sectionIndex];
      sectionDimensions["tw"] = tw[sectionIndex];
      sectionDimensions["t2b"] = t2b[sectionIndex];
      sectionDimensions["tfb"] = tfb[sectionIndex];
      sectionDimensions["area"] = area[sectionIndex];
    }
  }
}
