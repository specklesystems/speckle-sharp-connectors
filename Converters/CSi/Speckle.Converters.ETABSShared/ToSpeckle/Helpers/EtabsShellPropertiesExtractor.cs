using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Helpers;

public sealed class EtabsShellPropertiesExtractor
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public EtabsShellPropertiesExtractor(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ExtractProperties(CsiShellWrapper shell, Dictionary<string, object?> properties)
  {
    Dictionary<string, object?> objectId = new Dictionary<string, object?>();

    objectId["deisgnOrientation"] = GetDesignOrientation(shell);
    (objectId["label"], objectId["level"]) = GetLabelAndLevel(shell);

    properties["objectId"] = objectId;

    // TODO: Add other ETABS-specific properties
  }

  private (string label, string level) GetLabelAndLevel(CsiShellWrapper shell)
  {
    string label = string.Empty,
      level = string.Empty;
    _ = _settingsStore.Current.SapModel.AreaObj.GetLabelFromName(shell.Name, ref label, ref level);
    return (label, level);
  }

  private string GetDesignOrientation(CsiShellWrapper shell)
  {
    eAreaDesignOrientation designOrientation = eAreaDesignOrientation.Null;
    _ = _settingsStore.Current.SapModel.AreaObj.GetDesignOrientation(shell.Name, ref designOrientation);
    return designOrientation.ToString();
  }
}
