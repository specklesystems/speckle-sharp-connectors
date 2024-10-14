using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.BuiltElements;

[NameAndRankValue(nameof(CDB.Entity), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CivilEntityToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly BaseCurveExtractor _baseCurveExtractor;
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;

  public CivilEntityToSpeckleTopLevelConverter(
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    BaseCurveExtractor baseCurveExtractor,
    ClassPropertiesExtractor classPropertiesExtractor
  )
  {
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _baseCurveExtractor = baseCurveExtractor;
    _classPropertiesExtractor = classPropertiesExtractor;
  }

  public Base Convert(object target) => Convert((CDB.Entity)target);

  public Base Convert(CDB.Entity target)
  {
    Base civilObject = new();
    civilObject["type"] = target.GetType().ToString().Split('.').Last();
    civilObject["name"] = target.Name;
    civilObject["units"] = _settingsStore.Current.SpeckleUnits;

    // get basecurve
    List<ICurve>? baseCurves = _baseCurveExtractor.GetBaseCurve(target);
    if (baseCurves is not null)
    {
      civilObject["baseCurves"] = baseCurves;
    }

    // extract display value
    List<SOG.Mesh> display = _displayValueExtractor.GetDisplayValue(target);
    if (display.Count > 0)
    {
      civilObject["displayValue"] = display;
    }

    // add any additional class properties
    Dictionary<string, object?>? classProperties = _classPropertiesExtractor.GetClassProperties(target);
    if (classProperties is not null)
    {
      foreach (string key in classProperties.Keys)
      {
        civilObject[$"{key}"] = classProperties[key];
      }
    }

    return civilObject;
  }
}
