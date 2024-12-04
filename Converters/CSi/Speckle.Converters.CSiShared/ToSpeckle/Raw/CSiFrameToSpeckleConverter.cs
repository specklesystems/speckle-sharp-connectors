using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

public abstract class CSiFrameToSpeckleConverter
{
  protected CSiFrameToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
  {
    SettingsStore = settingsStore;
  }
  
  protected IConverterSettingsStore<CSiConversionSettings> SettingsStore { get; }

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

  protected virtual void AddProductSpecificClassProperties(CSiFrameWrapper frame, Dictionary<string, object> properties)
  { }
}
