using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

public class ShellToSpeckleConverter : CSiShellToSpeckleConverter
{
  public ShellToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore) : base(settingsStore)
  { }

  protected override void AddProductSpecificClassProperties(CSiShellWrapper shell,
    Dictionary<string, object> properties)
  {
    // Get label and story
    string label = "", story = "";
    _ = SettingsStore.Current.SapModel.AreaObj.GetLabelFromName(shell.Name, ref label, ref story);

    properties["label"] = label;
    properties["story"] = story;

    // Get design orientation
    eAreaDesignOrientation designOrientation = eAreaDesignOrientation.Null;
    _ = SettingsStore.Current.SapModel.AreaObj.GetDesignOrientation(shell.Name, ref designOrientation);
    
    properties["designOrientation"] = designOrientation.ToString();
  }
}
