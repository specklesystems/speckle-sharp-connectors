using Speckle.Connectors.CSiShared.HostApp.Helpers;

namespace Speckle.Connectors.ETABSShared.HostApp.Helpers;

/// <summary>
/// Coordinates property extraction combining base CSi and ETABS-specific properties.
/// </summary>
/// <remarks>
/// Mirrors property extraction system pattern used in EtabsPropertiesExtractor.
/// Composition handled at coordinator level rather than individual extractors.
/// </remarks>
public class EtabsSectionPropertyExtractor
{
  private readonly IFrameSectionPropertyExtractor _csiFrameExtractor;
  private readonly IShellSectionPropertyExtractor _csiShellExtractor;
  private readonly IApplicationFrameSectionPropertyExtractor _etabsFrameExtractor;
  private readonly IApplicationShellSectionPropertyExtractor _etabsShellExtractor;

  public EtabsSectionPropertyExtractor(
    IFrameSectionPropertyExtractor csiFrameExtractor,
    IShellSectionPropertyExtractor csiShellExtractor,
    IApplicationFrameSectionPropertyExtractor etabsFrameExtractor,
    IApplicationShellSectionPropertyExtractor etabsShellExtractor
  )
  {
    _csiFrameExtractor = csiFrameExtractor;
    _csiShellExtractor = csiShellExtractor;
    _etabsFrameExtractor = etabsFrameExtractor;
    _etabsShellExtractor = etabsShellExtractor;
  }

  public SectionPropertyExtractionResult ExtractFrameSectionProperties(string sectionName)
  {
    SectionPropertyExtractionResult propertyExtraction = new();
    _csiFrameExtractor.ExtractProperties(sectionName, propertyExtraction);
    _etabsFrameExtractor.ExtractProperties(sectionName, propertyExtraction);
    return propertyExtraction;
  }

  public SectionPropertyExtractionResult ExtractShellSectionProperties(string sectionName)
  {
    SectionPropertyExtractionResult propertyExtraction = new();
    _csiShellExtractor.ExtractProperties(sectionName, propertyExtraction);
    _etabsShellExtractor.ExtractProperties(sectionName, propertyExtraction);
    return propertyExtraction;
  }
}
