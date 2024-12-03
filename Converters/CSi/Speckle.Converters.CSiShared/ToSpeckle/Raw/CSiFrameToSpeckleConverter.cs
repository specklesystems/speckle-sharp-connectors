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
   
    // TODO: Add common properties
    properties["name"] = frame.Name;

  }

  protected virtual void AddProductSpecificClassProperties(CSiFrameWrapper frame, Dictionary<string, object> properties)
  { }
}
