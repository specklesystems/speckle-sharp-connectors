using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.ToSpeckle.Raw;

namespace Speckle.Converters.ETABSShared.ToSpeckle.Raw;

public class JointToSpeckleConverter : CSiJointToSpeckleConverter
{
  public JointToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore) : base(settingsStore)
  {
    
  }
}
