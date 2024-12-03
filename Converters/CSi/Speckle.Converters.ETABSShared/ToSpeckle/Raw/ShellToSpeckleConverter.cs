using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

public class ShellToSpeckleConverter : CSiShellToSpeckleConverter
{
  public ShellToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore) : base(settingsStore)
  {
    
  }
}
