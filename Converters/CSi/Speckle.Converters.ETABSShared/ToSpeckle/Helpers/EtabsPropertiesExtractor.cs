using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

/// <summary>
/// ETABS-specific property extractor that composes with SharedPropertiesExtractor to provide
/// both shared and ETABS-specific properties.
/// </summary>
/// <remarks>
/// Follows the composition pattern where SharedPropertiesExtractor handles common CSI properties,
/// while this class adds ETABS-specific properties. The extraction order is important:
/// 1. Extract shared properties first via SharedPropertiesExtractor
/// 2. Augment with ETABS-specific properties
/// This ensures consistent base properties with ETABS-specific enrichment.
/// </remarks>
public class EtabsPropertiesExtractor : IApplicationPropertiesExtractor
{
  public SharedPropertiesExtractor SharedPropertiesExtractor { get; }
  private readonly EtabsFramePropertiesExtractor _etabsFramePropertiesExtractor;
  private readonly EtabsJointPropertiesExtractor _etabsJointPropertiesExtractor;
  private readonly EtabsShellPropertiesExtractor _etabsShellPropertiesExtractor;

  public EtabsPropertiesExtractor(
    SharedPropertiesExtractor sharedPropertiesExtractor,
    EtabsFramePropertiesExtractor etabsFramePropertiesExtractor,
    EtabsJointPropertiesExtractor etabsJointPropertiesExtractor,
    EtabsShellPropertiesExtractor etabsShellPropertiesExtractor
  )
  {
    SharedPropertiesExtractor = sharedPropertiesExtractor;
    _etabsFramePropertiesExtractor = etabsFramePropertiesExtractor;
    _etabsJointPropertiesExtractor = etabsJointPropertiesExtractor;
    _etabsShellPropertiesExtractor = etabsShellPropertiesExtractor;
  }

  public PropertyExtractionResult ExtractProperties(ICsiWrapper wrapper)
  {
    // Extract shared properties first
    var propertiesExtractionResult = SharedPropertiesExtractor.Extract(wrapper);

    // Then we go into Etabs-specific stuff
    switch (wrapper)
    {
      case CsiFrameWrapper frame:
        _etabsFramePropertiesExtractor.ExtractProperties(frame, propertiesExtractionResult.Properties);
        break;
      case CsiJointWrapper joint:
        _etabsJointPropertiesExtractor.ExtractProperties(joint, propertiesExtractionResult.Properties);
        break;
      case CsiShellWrapper shell:
        _etabsShellPropertiesExtractor.ExtractProperties(shell, propertiesExtractionResult.Properties);
        break;
    }

    return propertiesExtractionResult;
  }
}
