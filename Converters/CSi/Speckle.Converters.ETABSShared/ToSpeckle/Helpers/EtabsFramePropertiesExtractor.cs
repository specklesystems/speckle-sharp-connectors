using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;

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
    var objectId = DictionaryUtils.EnsureNestedDictionary(properties, "objectId");
    objectId["designOrientation"] = GetDesignOrientation(frame);
    (objectId["label"], objectId["level"]) = GetLabelAndLevel(frame);

    var design = DictionaryUtils.EnsureNestedDictionary(properties, "design");
    design["designProcedure"] = GetDesignProcedure(frame);
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

  private string GetDesignProcedure(CsiFrameWrapper frame)
  {
    int myType = 0;
    _ = _settingsStore.Current.SapModel.FrameObj.GetDesignProcedure(frame.Name, ref myType);
    return myType switch
    {
      1 => "Steel Frame Design",
      2 => "Concrete Frame Design",
      3 => "Composite Beam Design",
      4 => "Steel Joist Design",
      7 => "No Design",
      13 => "Composite Column Design",
      _ => "Program determined"
    };
  }
}
