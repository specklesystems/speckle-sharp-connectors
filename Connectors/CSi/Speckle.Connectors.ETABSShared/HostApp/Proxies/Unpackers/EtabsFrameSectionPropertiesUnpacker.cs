using Microsoft.Extensions.Logging;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.ETABSShared.HostApp;

/// <summary>
/// Etabs-specific implementation for extracting frame section properties from Etabs.
/// </summary>
/// <remarks>
/// Extends the base frame section properties extractor with Etabs-specific property extraction logic.
/// Leverages Etabs API's GetAllFrameProperties_2 method to retrieve comprehensive section details.
/// This method is not documented for Sap2000 and can therefore not be included in the CsiShared project
/// Follows the template method pattern to allow customization of property extraction.
/// </remarks>
public class EtabsFrameSectionPropertiesUnpacker : FrameSectionPropertiesUnpacker
{
  private readonly ICsiApplicationService _csiApplicationService;
  private readonly ILogger<EtabsFrameSectionPropertiesUnpacker> _logger;

  public EtabsFrameSectionPropertiesUnpacker(
    IConverterSettingsStore<CsiConversionSettings> settingsStore,
    ILogger<EtabsFrameSectionPropertiesUnpacker> logger,
    ICsiApplicationService csiApplicationService
  )
    : base(settingsStore, logger)
  {
    _csiApplicationService = csiApplicationService;
    _logger = logger;
  }

  protected override void ExtractTypeSpecificProperties(string sectionName, Dictionary<string, object?> properties)
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

    _csiApplicationService.SapModel.PropFrame.GetAllFrameProperties_2(
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
      var generalData = DictionaryUtils.EnsureNestedDictionary(properties, "General Data");
      generalData["type"] = propTypes[sectionIndex].ToString();

      // Section Dimensions
      var sectionDimensions = DictionaryUtils.EnsureNestedDictionary(properties, "Section Dimensions");
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
