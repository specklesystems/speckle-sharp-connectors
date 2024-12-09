using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

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
