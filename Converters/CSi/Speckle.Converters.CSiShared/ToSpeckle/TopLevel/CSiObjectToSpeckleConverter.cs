using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Data; // This will come
using Speckle.Sdk.Models;

namespace Speckle.Converters.CSiShared.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(ICSiObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CSiObjectToSpeckleConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;

  public CSiObjectToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Base Convert(object target)
  {
    if (target is not ICSiObject csiObject)
    {
      throw new ArgumentException($"Target object is not a CSi object. It's a {target.GetType()}");
    }

    var result = new CSiObject // This should be coming from sdk
    {
      type = target.GetType().ToString().Split('.').Last(),
      units = _settingsStore.Current.SpeckleUnits,
      name = csiObject.name,
      displayValue = new List<Base>()
    };

    // Get properties (material, section, etc.)
    // _propertyExtractor, ObjectPropertyExtractor or similar in a Helpers folder?

    // Get display value (geometry)
    // _displayValueExtractor DisplayValueExtractor or SpatialDataExtractor or GeometricDataExtractor in a Helpers folder


    return result;
  }
}
