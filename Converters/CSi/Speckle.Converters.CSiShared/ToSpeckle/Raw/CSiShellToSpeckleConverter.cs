using Speckle.Converters.Common;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

public abstract class CSiShellToSpeckleConverter
{
  protected CSiShellToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
  {
    SettingsStore = settingsStore;
  }
  
  protected IConverterSettingsStore<CSiConversionSettings> SettingsStore { get; }

  public Dictionary<string, object> GetClassProperties(CSiShellWrapper shell)
  {
    var properties = new Dictionary<string, object>();
        
    AddCommonClassProperties(shell, properties);
    AddProductSpecificClassProperties(shell, properties);

    return properties;
  }

  private void AddCommonClassProperties(CSiShellWrapper shell, Dictionary<string, object> properties)
  {
    // TODO: As part of data extraction. But a placeholder example below:
    int numberGroups = 0;
    string[] groups = Array.Empty<string>();
    
    int result = SettingsStore.Current.SapModel.AreaObj.GetGroupAssign(shell.Name, ref numberGroups, ref groups);

    if (result == 0 && groups.Length > 0)
    {
      properties["groupAssigns"] = new List<string>(groups);
    }
  }

  protected virtual void AddProductSpecificClassProperties(CSiShellWrapper shell, Dictionary<string, object> properties)
  { }
}
