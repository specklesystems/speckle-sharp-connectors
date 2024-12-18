using Speckle.Converters.ArcGIS3.ToSpeckle.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.Registration;
using Speckle.Objects.Data;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(AC.CoreObjectsBase), 0)]
public class CoreObjectsBaseToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public CoreObjectsBaseToSpeckleTopLevelConverter(
    DisplayValueExtractor displayValueExtractor,
    PropertiesExtractor propertiesExtractor,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _propertiesExtractor = propertiesExtractor;
    _settingsStore = settingsStore;
  }

  public BaseResult Convert(object target) => BaseResult.Success( Convert((AC.CoreObjectsBase)target));

  private ArcgisObject Convert(AC.CoreObjectsBase target)
  {
    string type = target.GetType().Name;

    // get display value
    List<Base> display = _displayValueExtractor.GetDisplayValue(target).ToList();

    // get properties
    Dictionary<string, object?> properties = _propertiesExtractor.GetProperties(target);

    ArcgisObject result =
      new()
      {
        name = type,
        type = type,
        displayValue = display,
        properties = properties,
        units = _settingsStore.Current.SpeckleUnits,
        applicationId = ""
      };

    return result;
  }
}
