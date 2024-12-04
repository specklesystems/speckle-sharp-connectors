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

  /// <summary>
  /// Converts CSi objects to Speckle format, extracting properties, display geometry and application IDs.
  /// </summary>
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

  /// <remarks>
  /// This will be refined! Just a POC for now. Data Extraction (Send) milestone will incorporate improvements here.
  /// </remarks>
  private Base Convert(CSiWrapperBase target) // TODO: CSiObject and not Base pending SDK updates.
  {
    var result = new Base
    {
      ["name"] = target.Name,
      ["type"] = target.ObjectName,
      ["units"] = _settingsStore.Current.SpeckleUnits,
      ["properties"] = _classPropertyExtractor.GetProperties(target),
      ["displayValue"] = _displayValueExtractor.GetDisplayValue(target).ToList()
    };

    string applicationId = ""; // TODO: Investigate the GUIDs coming through
    if (target is CSiJointWrapper) // TODO: Surely there is a better way of doing this? Gross.
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
