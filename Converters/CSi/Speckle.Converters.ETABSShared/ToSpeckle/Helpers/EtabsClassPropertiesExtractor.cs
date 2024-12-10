using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// Extracts ETABS-specific properties from CSI objects.
/// Implements IClassPropertyExtractor to provide product-specific property extraction.
/// </summary>
/// <remarks>
/// Responsibilities:
/// - Delegates property extraction to specialized extractors based on object type
/// - Complements <see cref="CsiGeneralPropertiesExtractor"/> by adding ETABS-specific data
/// - Maintains separation between common CSI properties and ETABS-specific properties
///
/// Design Pattern:
/// - Uses composition and dependency injection for specialized extractors
/// - Switch pattern matches wrapper types to appropriate extractors
/// - Part of the overall property extraction hierarchy:
///   * IPropertyExtractor (base interface)
///   * IClassPropertyExtractor (this implementation - ETABS specific)
///   * IGeneralPropertyExtractor (CSI common properties)
///
/// Integration:
/// - Used by PropertiesExtractor alongside CsiGeneralPropertiesExtractor
/// - Each specialized extractor (Frame/Joint/Shell) handles its own ETABS-specific API calls
/// </remarks>
public class EtabsClassPropertiesExtractor : IClassPropertyExtractor
{
  private readonly EtabsFramePropertiesExtractor _etabsFramePropertiesExtractor;
  private readonly EtabsJointPropertiesExtractor _etabsJointPropertiesExtractor;
  private readonly EtabsShellPropertiesExtractor _etabsShellPropertiesExtractor;

  public EtabsClassPropertiesExtractor(
    EtabsFramePropertiesExtractor etabsFramePropertiesExtractor,
    EtabsJointPropertiesExtractor etabsJointPropertiesExtractor,
    EtabsShellPropertiesExtractor etabsShellPropertiesExtractor
  )
  {
    _etabsFramePropertiesExtractor = etabsFramePropertiesExtractor;
    _etabsJointPropertiesExtractor = etabsJointPropertiesExtractor;
    _etabsShellPropertiesExtractor = etabsShellPropertiesExtractor;
  }

  public void ExtractProperties(ICsiWrapper wrapper, Dictionary<string, object?> properties)
  {
    switch (wrapper)
    {
      case CsiFrameWrapper frame:
        _etabsFramePropertiesExtractor.ExtractProperties(frame, properties);
        break;
      case CsiJointWrapper joint:
        _etabsJointPropertiesExtractor.ExtractProperties(joint, properties);
        break;
      case CsiShellWrapper shell:
        _etabsShellPropertiesExtractor.ExtractProperties(shell, properties);
        break;
    }
  }
}
