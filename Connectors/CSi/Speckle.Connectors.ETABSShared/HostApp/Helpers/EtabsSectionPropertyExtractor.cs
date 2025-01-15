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

  /// <summary>
  /// Extract the properties on both a Csi and app-specific level
  /// </summary>
  /// <remarks>
  /// SectionPropertyExtractionResult formalises and enforces (somewhat) the required attributes
  /// propertyExtraction gets mutated within the _csiFrameExtractor and _etabsFrameExtractor methods
  /// Not ideal, BUT this way we negate specific order of operations AND it create uniformity in the approach
  /// with shell sections although how obtain MaterialName (for example) differs between the two types.
  /// For FRAME, the material is obtained easily on the CsiShared level
  /// </remarks>
  public SectionPropertyExtractionResult ExtractFrameSectionProperties(string sectionName)
  {
    SectionPropertyExtractionResult propertyExtraction = new();
    _csiFrameExtractor.ExtractProperties(sectionName, propertyExtraction);
    _etabsFrameExtractor.ExtractProperties(sectionName, propertyExtraction);
    return propertyExtraction;
  }

  /// <summary>
  /// Extract the properties on both a Csi and app-specific level
  /// </summary>
  /// <remarks>
  /// SectionPropertyExtractionResult formalises and enforces (somewhat) the required attributes
  /// propertyExtraction gets mutated within the _csiShellExtractor and _etabsShellExtractor methods
  /// Not ideal, BUT this way we negate specific order of operations AND it create uniformity in the approach
  /// with frame sections although how obtain MaterialName (for example) differs between the two types.
  /// Property extraction is complicated for shells, see EtabsShellSectionResolver.
  /// </remarks>
  public SectionPropertyExtractionResult ExtractShellSectionProperties(string sectionName)
  {
    SectionPropertyExtractionResult propertyExtraction = new();
    _csiShellExtractor.ExtractProperties(sectionName, propertyExtraction);
    _etabsShellExtractor.ExtractProperties(sectionName, propertyExtraction);
    return propertyExtraction;
  }
}
