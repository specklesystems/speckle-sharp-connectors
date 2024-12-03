using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

public class FrameToSpeckleConverter : CSiFrameToSpeckleConverter
{
  public FrameToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore) 
    : base(settingsStore)
  {
  }

  protected override void AddProductSpecificClassProperties(CSiFrameWrapper frame, Dictionary<string, object> properties)
  {
    // Get label and story
    string label = "", story = "";
    _ = SettingsStore.Current.SapModel.FrameObj.GetLabelFromName(frame.Name, ref label, ref story);

    properties["label"] = label;
    properties["story"] = story;

    // Get design orientation
    eFrameDesignOrientation designOrientation = eFrameDesignOrientation.Null;
    _ = SettingsStore.Current.SapModel.FrameObj.GetDesignOrientation(frame.Name, ref designOrientation);
    
    properties["designOrientation"] = designOrientation.ToString();
  }
}
