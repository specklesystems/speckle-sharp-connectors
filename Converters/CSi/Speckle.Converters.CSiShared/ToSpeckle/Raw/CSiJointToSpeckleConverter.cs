using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

public abstract class CSiJointToSpeckleConverter
{
  protected CSiJointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
  {
    SettingsStore = settingsStore;
  }
  
  protected IConverterSettingsStore<CSiConversionSettings> SettingsStore { get; }

  public Dictionary<string, object> GetClassProperties(CSiJointWrapper joint)
  {
    var properties = new Dictionary<string, object>();
        
    AddCommonClassProperties(joint, properties);
    AddProductSpecificClassProperties(joint, properties);

    return properties;
  }

  private void AddCommonClassProperties(CSiJointWrapper joint, Dictionary<string, object> properties)
  {
    properties["name"] = joint.Name;
  }

  protected virtual void AddProductSpecificClassProperties(CSiJointWrapper joint, Dictionary<string, object> properties)
  { }
}
