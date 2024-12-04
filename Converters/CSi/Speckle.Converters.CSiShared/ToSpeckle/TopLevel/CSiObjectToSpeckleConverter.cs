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
      ["type"] = target.ObjectName,
      ["units"] = _settingsStore.Current.SpeckleUnits,
      ["properties"] = _classPropertyExtractor.GetProperties(target),
      ["displayValue"] = _displayValueExtractor.GetDisplayValue(target).ToList()
    };

    string applicationId = "";
    if (target is CSiJointWrapper)
    {
      _ = _settingsStore.Current.SapModel.PointObj.GetGUID(target.Name, ref applicationId);
    }
    else if (target is CSiFrameWrapper)
    {
      _ = _settingsStore.Current.SapModel.FrameObj.GetGUID(target.Name, ref applicationId);
    }
    else if (target is CSiShellWrapper)
    {
      _ = _settingsStore.Current.SapModel.AreaObj.GetGUID(target.Name, ref applicationId);
    }
    result["applicationId"] = applicationId;

    return result;
  }
}
