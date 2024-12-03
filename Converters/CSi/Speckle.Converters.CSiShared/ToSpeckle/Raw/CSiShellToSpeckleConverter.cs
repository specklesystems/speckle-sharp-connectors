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
    properties["name"] = shell.Name;
  }

  protected virtual void AddProductSpecificClassProperties(CSiShellWrapper shell, Dictionary<string, object> properties)
  { }
}
