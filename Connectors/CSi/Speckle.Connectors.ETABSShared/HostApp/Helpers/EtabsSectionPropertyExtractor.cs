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
  /// Extract the frame section properties on both a Csi and app-specific level
  /// </summary>
  public Dictionary<string, object?> ExtractFrameSectionProperties(string sectionName)
  {
    Dictionary<string, object?> properties = [];
    _csiFrameExtractor.ExtractProperties(sectionName, properties);
    _etabsFrameExtractor.ExtractProperties(sectionName, properties);
    return properties;
  }

  /// <summary>
  /// Extract the shell section properties on both a Csi and app-specific level
  /// </summary>
  public Dictionary<string, object?> ExtractShellSectionProperties(string sectionName)
  {
    Dictionary<string, object?> properties = [];
    _csiShellExtractor.ExtractProperties(sectionName, properties);
    _etabsShellExtractor.ExtractProperties(sectionName, properties);
    return properties;
  }
}
