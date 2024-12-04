using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

/// <summary>
/// ETABS-specific frame property converter that extends the base CSi implementation.
/// Adds ETABS-specific properties such as story assignment and design orientation.
/// </summary>
/// <remarks>
/// Additional properties extracted:
/// - label: User-defined label for the frame
/// - story: Story/level assignment of the frame
/// - designOrientation: Frame classification (Column, Beam, Brace, etc.)
/// These properties are used to organize the model structure in collections and provide ETABS-specific information.
/// This information is not available in SAP 2000.
/// </remarks>
public class FrameToSpeckleConverter : CSiFrameToSpeckleConverter
{
  public FrameToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore) 
    : base(settingsStore)
  {
  }

  /// <summary>
  /// Adds ETABS-specific properties to the frame object.
  /// </summary>
  /// <param name="frame">The frame wrapper containing the ETABS object reference</param>
  /// <param name="properties">Dictionary to store the extracted properties</param>
  /// <remarks>
  /// This structure for AddProductSpecificClassProperties() is exactly same for Frame, Joint and Shell. Write better?
  /// </remarks>
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
