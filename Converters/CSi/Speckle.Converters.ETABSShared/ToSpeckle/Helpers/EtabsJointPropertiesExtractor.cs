using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

public sealed class EtabsJointPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsJointPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiJointWrapper joint, Dictionary<string, object?> properties)
  {
    Dictionary<string, object?> objectId = new Dictionary<string, object?>();

    (objectId["label"], objectId["level"]) = GetLabelAndLevel(joint);

    properties["objectId"] = objectId;

    // TODO: Add other ETABS-specific properties
  }

  private (string label, string level) GetLabelAndLevel(CsiJointWrapper joint)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.PointObj.GetLabelFromName(joint.Name, ref label, ref level);
    return (label, level);
  }
}
