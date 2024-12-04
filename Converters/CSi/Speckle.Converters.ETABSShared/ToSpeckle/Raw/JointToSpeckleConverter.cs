using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

public class JointToSpeckleConverter : CSiJointToSpeckleConverter
{
  public JointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore) : base(settingsStore)
  { }

  protected override void AddProductSpecificClassProperties(CSiJointWrapper joint,
    Dictionary<string, object> properties)
  {
    // Get label and story
    string label = "", story = "";
    _ = SettingsStore.Current.SapModel.PointObj.GetLabelFromName(joint.Name, ref label, ref story);

    properties["label"] = label;
    properties["story"] = story;
    
    // Diaphragm assignments
    eDiaphragmOption diaphragmOption = eDiaphragmOption.Disconnect;
    string diaphragmName = "";
    _ = SettingsStore.Current.SapModel.PointObj.GetDiaphragm(joint.Name, ref diaphragmOption, ref diaphragmName);

    properties["diaphragm"] = diaphragmName.ToString();
  }
}
