using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

/// <summary>
/// Base converter for extracting frame properties common to all CSi applications.
/// Product-specific implementations (ETABS, SAP2000) can extend this to add their own properties.
/// </summary>
public abstract class CSiFrameToSpeckleConverter
{
  protected CSiFrameToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
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
  public Dictionary<string, object> GetClassProperties(CSiFrameWrapper frame)
  {
    var properties = new Dictionary<string, object>();

    AddCommonClassProperties(frame, properties);
    AddProductSpecificClassProperties(frame, properties);

    return properties;
  }

  private void AddCommonClassProperties(CSiFrameWrapper frame, Dictionary<string, object> properties)
  {
    // TODO: As part of data extraction. But a placeholder example below:
    int numberGroups = 0;
    string[] groups = Array.Empty<string>();

    int result = SettingsStore.Current.SapModel.FrameObj.GetGroupAssign(frame.Name, ref numberGroups, ref groups);

    if (result == 0 && groups.Length > 0)
    {
      properties["groupAssigns"] = new List<string>(groups);
    }
  }

  protected virtual void AddProductSpecificClassProperties(
    CSiFrameWrapper frame,
    Dictionary<string, object> properties
  ) { }
}
