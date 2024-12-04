using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

/// <summary>
/// ETABS-specific shell property converter that extends the base CSi implementation.
/// Adds ETABS-specific properties such as story assignment and design orientation.
/// </summary>
/// <remarks>
/// Additional properties extracted:
/// - label: User-defined label for the shell
/// - story: Story/level assignment of the shell
/// - designOrientation: Shell classification (Wall, Floor, Null, etc.)
/// These properties are used to organize the model structure in collections and provide ETABS-specific information.
/// This information is not available in SAP 2000.
/// </remarks>
public class ShellToSpeckleConverter : CSiShellToSpeckleConverter
{
  public ShellToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
    : base(settingsStore) { }

  /// <summary>
  /// Adds ETABS-specific properties to the shell object.
  /// </summary>
  /// <param name="shell">The shell wrapper containing the ETABS object reference</param>
  /// <param name="properties">Dictionary to store the extracted properties</param>
  /// <remarks>
  /// This structure for AddProductSpecificClassProperties() is exactly same for Frame, Joint and Shell. Write better?
  /// </remarks>
  protected override void AddProductSpecificClassProperties(
    CSiShellWrapper shell,
    Dictionary<string, object> properties
  )
  {
    string label = "",
      story = "";
    _ = SettingsStore.Current.SapModel.AreaObj.GetLabelFromName(shell.Name, ref label, ref story);

    properties["label"] = label;
    properties["story"] = story;

    eAreaDesignOrientation designOrientation = eAreaDesignOrientation.Null;
    _ = SettingsStore.Current.SapModel.AreaObj.GetDesignOrientation(shell.Name, ref designOrientation);

    properties["designOrientation"] = designOrientation.ToString();
  }
}
