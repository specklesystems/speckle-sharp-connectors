using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

/// <summary>
/// ETABS-specific joint property converter that extends the base CSi implementation.
/// Adds ETABS-specific properties such as story assignment and design orientation.
/// </summary>
/// <remarks>
/// Additional properties extracted:
/// - label: User-defined label for the joint
/// - story: Story/level assignment of the joint
/// - diaphragm: Joint diaphragm assignment
/// These properties are used to organize the model structure in collections and provide ETABS-specific information.
/// This information is not available in SAP 2000.
/// </remarks>
public class JointToSpeckleConverter : CSiJointToSpeckleConverter
{
  public JointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
    : base(settingsStore) { }

  /// <summary>
  /// Adds ETABS-specific properties to the joint object.
  /// </summary>
  /// <param name="joint">The joint wrapper containing the ETABS object reference</param>
  /// <param name="properties">Dictionary to store the extracted properties</param>
  /// <remarks>
  /// This structure for AddProductSpecificClassProperties() is exactly same for Frame, Joint and Shell. Write better?
  /// </remarks>
  protected override void AddProductSpecificClassProperties(
    CSiJointWrapper joint,
    Dictionary<string, object> properties
  )
  {
    // Get label and story
    string label = "",
      story = "";
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
