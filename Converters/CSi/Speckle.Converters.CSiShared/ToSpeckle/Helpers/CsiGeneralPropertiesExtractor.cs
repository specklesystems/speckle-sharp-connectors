using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Helpers;

public class CsiGeneralPropertiesExtractor : IGeneralPropertyExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public CsiGeneralPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
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

    // Application ID (Csi GUID)
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetGUID(frame.Name, ref applicationId);
    properties["applicationId"] = applicationId;

    // TODO: More properties

    return properties;
  }

  private Dictionary<string, object?>? ExtractJointProperties(CsiJointWrapper joint)
  {
    var properties = new Dictionary<string, object?>();

    // Application ID (Csi GUID)
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetGUID(joint.Name, ref applicationId);

    properties["applicationId"] = applicationId;

    // TODO: More properties

    return properties;
  }

  private Dictionary<string, object?>? ExtractShellProperties(CsiShellWrapper shell)
  {
    var properties = new Dictionary<string, object?>();

    // Application ID (Csi GUID)
    string applicationId = string.Empty;
    _ = _settingsStore.Current.SapModel.FrameObj.GetGUID(shell.Name, ref applicationId);

    properties["applicationId"] = applicationId;

    // TODO: More properties

    return properties;
  }
}
