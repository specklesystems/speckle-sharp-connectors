using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

public sealed class EtabsFramePropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsFramePropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiFrameWrapper frame, Dictionary<string, object?> properties)
  {
    Dictionary<string, object?> objectId = new Dictionary<string, object?>();

    objectId["deisgnOrientation"] = GetDesignOrientation(frame);
    (objectId["label"], objectId["level"]) = GetLabelAndLevel(frame);

    properties["objectId"] = objectId;

    // TODO: Add other ETABS-specific properties
  }

  private (string label, string level) GetLabelAndLevel(CsiFrameWrapper frame)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetLabelFromName(frame.Name, ref label, ref level);
    return (label, level);
  }

  private string GetDesignOrientation(CsiFrameWrapper frame)
  {
    eFrameDesignOrientation designOrientation = eFrameDesignOrientation.Null;
    _ = _settingsStore.Current.SapModel.FrameObj.GetDesignOrientation(frame.Name, ref designOrientation);
    return designOrientation.ToString();
  }
}
