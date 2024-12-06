using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

/// <summary>
/// Base converter for extracting joint properties common to all CSi applications.
/// Product-specific implementations (ETABS, SAP2000) can extend this to add their own properties.
/// </summary>
public abstract class CSiJointToSpeckleConverter
{
  protected CSiJointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
  {
    SettingsStore = settingsStore;
  }

  protected IConverterSettingsStore<CSiConversionSettings> SettingsStore { get; }

  /// <summary>
  /// Extracts both common and product-specific properties from a frame element.
  /// Product-specific properties are defined by derived classes.
  /// </summary>
  /// <remarks>
  /// This will be refined! Just a POC for now. Data Extraction (Send) milestone will incorporate improvements here.
  /// </remarks>
  public Dictionary<string, object> GetClassProperties(CSiJointWrapper joint)
  {
    var properties = new Dictionary<string, object>();

    AddCommonClassProperties(joint, properties);
    AddProductSpecificClassProperties(joint, properties);

    return properties;
  }

  private void AddCommonClassProperties(CSiJointWrapper joint, Dictionary<string, object> properties)
  {
    // TODO: As part of data extraction. But a placeholder example below:
    int numberGroups = 0;
    string[] groups = Array.Empty<string>();

    int result = SettingsStore.Current.SapModel.FrameObj.GetGroupAssign(joint.Name, ref numberGroups, ref groups);

    if (result == 0 && groups.Length > 0)
    {
      properties["groupAssigns"] = new List<string>(groups);
    }
  }

  protected virtual void AddProductSpecificClassProperties(
    CSiJointWrapper joint,
    Dictionary<string, object> properties
  ) { }
}
