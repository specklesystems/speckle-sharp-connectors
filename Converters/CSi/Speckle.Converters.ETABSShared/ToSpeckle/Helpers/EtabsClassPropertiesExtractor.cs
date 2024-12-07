using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

public class EtabsClassPropertiesExtractor : IClassPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsClassPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Dictionary<string, object?>? ExtractProperties(ICsiWrapper wrapper)
  {
    return wrapper switch
    {
      CsiFrameWrapper frame => ExtractFrameProperties(frame),
      CsiJointWrapper joint => ExtractJointProperties(joint),
      CsiShellWrapper shell => ExtractShellProperties(shell),
      _ => null
    };
  }

  private Dictionary<string, object?>? ExtractFrameProperties(CsiFrameWrapper frame)
  {
    var properties = new Dictionary<string, object?>();

    // Get the level associated with the frame
    string label = "",
      level = "";
    if (_settingsStore.Current.SapModel.FrameObj.GetLabelFromName(frame.Name, ref label, ref level) == 0)
    {
      properties["label"] = label;
      properties["level"] = level;
    }

    // The design orientation further classifies frames into column, beam, brace or null
    eFrameDesignOrientation designOrientation = eFrameDesignOrientation.Null;
    if (_settingsStore.Current.SapModel.FrameObj.GetDesignOrientation(frame.Name, ref designOrientation) == 0)
    {
      properties["designOrientation"] = designOrientation.ToString();
    }

    // Add other ETABS-specific properties
    return properties;
  }

  private Dictionary<string, object?>? ExtractJointProperties(CsiJointWrapper joint)
  {
    var properties = new Dictionary<string, object?>();

    // Get the level associated with the joint
    string label = "",
      level = "";
    if (_settingsStore.Current.SapModel.PointObj.GetLabelFromName(joint.Name, ref label, ref level) == 0)
    {
      properties["label"] = label;
      properties["level"] = level;
    }

    // Add other ETABS-specific properties
    return properties;
  }

  private Dictionary<string, object?>? ExtractShellProperties(CsiShellWrapper shell)
  {
    var properties = new Dictionary<string, object?>();

    // Get the level associated with the frame
    string label = "",
      level = "";
    if (_settingsStore.Current.SapModel.AreaObj.GetLabelFromName(shell.Name, ref label, ref level) == 0)
    {
      properties["label"] = label;
      properties["level"] = level;
    }

    // The design orientation further classifies shells into floor, wall, ramp, null etc.
    eAreaDesignOrientation designOrientation = eAreaDesignOrientation.Null;
    if (_settingsStore.Current.SapModel.AreaObj.GetDesignOrientation(shell.Name, ref designOrientation) == 0)
    {
      properties["designOrientation"] = designOrientation.ToString();
    }

    // Add other ETABS-specific properties
    return properties;
  }
}
