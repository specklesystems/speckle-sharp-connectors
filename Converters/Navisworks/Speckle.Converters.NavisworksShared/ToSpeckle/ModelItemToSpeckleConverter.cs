using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converter.Navisworks.ToSpeckle;

public class ModelItemToSpeckleTopLevelConverter(IConverterSettingsStore<NavisworksConversionSettings> settingsStore)
  : IToSpeckleTopLevelConverter
{
  public SSM.Base Convert(object target) => new() { ["units"] = settingsStore.Current.SpeckleUnits };
}
