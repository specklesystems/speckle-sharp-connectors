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

  // TODO: _propertyExtractor

  public CSiObjectToSpeckleConverter(
    IConverterSettingsStore<CSiConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor
  // TODO: _propertyExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    // TODO: _property_extractor
  }

  public Base Convert(object target)
  {
    if (target is not ICSiWrapper csiWrapper)
    {
      throw new ArgumentException($"Target object must be a CSi wrapper. Got {target.GetType()}");
    }

    var result = new CSiObject
    {
      name = csiWrapper.Name,
      type = csiWrapper.GetType().ToString().Split('.').Last().Replace("Wrapper", ""), // CSiJointWrapper → CSiJoint, CSiFrameWrapper → CSiFrame etc.
      units = _settingsStore.Current.SpeckleUnits,
      // TODO: properties
      displayValue = _displayValueExtractor.GetDisplayValue(csiWrapper).ToList()
    };

    return result;
  }
}
