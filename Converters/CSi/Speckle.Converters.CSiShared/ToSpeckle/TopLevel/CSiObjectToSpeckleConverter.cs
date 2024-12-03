using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(CSiWrapperBase), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CSiObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ClassPropertyExtractor _classPropertyExtractor;

  public CSiObjectToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    ClassPropertyExtractor classPropertyExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _classPropertyExtractor = classPropertyExtractor;
  }

  public Base Convert(object target) => Convert((CSiWrapperBase)target);

  private Base Convert(CSiWrapperBase target)
  {
    var result = new Base
    {
      ["name"] = target.Name,
      ["type"] = target.GetType().ToString().Split('.').Last().Replace("Wrapper", ""), // CSiJointWrapper → CSiJoint, CSiFrameWrapper → CSiFrame etc.
      ["units"] = _settingsStore.Current.SpeckleUnits,
      ["properties"] = _classPropertyExtractor.GetProperties(target),
      ["displayValue"] = _displayValueExtractor.GetDisplayValue(target).ToList()
    };

    return result;
  }
}
