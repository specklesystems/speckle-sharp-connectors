using Speckle.Converters.Civil3dShared.Helpers;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;
using AECPropDB = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Speckle.Converters.Civil3dShared.ToSpeckle.BuiltElements;

[NameAndRankValue(nameof(CDB.Entity), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CivilEntityToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<AECPropDB.PropertySet, List<DataField>> _propertySetConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly BaseCurveExtractor _baseCurveExtractor;

  public CivilEntityToSpeckleTopLevelConverter(
    ITypedConverter<AECPropDB.PropertySet, List<DataField>> propertySetConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    BaseCurveExtractor baseCurveExtractor
  )
  {
    _propertySetConverter = propertySetConverter;
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _baseCurveExtractor = baseCurveExtractor;
  }

  public Base Convert(object target) => Convert((CDB.Entity)target);

  public Base Convert(CDB.Entity target)
  {
    Base civilObject = new();
    civilObject["type"] = target.GetType().ToString();
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

    return civilObject;
  }
}
