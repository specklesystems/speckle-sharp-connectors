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
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AECPropDB.PropertySet, List<DataField>> _propertySetConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly BaseCurveExtractor _baseCurveExtractor;

  public CivilEntityToSpeckleTopLevelConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AECPropDB.PropertySet, List<DataField>> propertySetConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore,
    DisplayValueExtractor displayValueExtractor,
    BaseCurveExtractor baseCurveExtractor
  )
  {
    _pointConverter = pointConverter;
    _propertySetConverter = propertySetConverter;
    _settingsStore = settingsStore;
    _displayValueExtractor = displayValueExtractor;
    _baseCurveExtractor = baseCurveExtractor;
  }

  public Base Convert(object target) => Convert((CDB.Entity)target);

  public Base Convert(CDB.Entity target)
  {
    Base civilObject = new();
    civilObject["category"] = target.GetType().ToString();
    civilObject["name"] = target.Name;
    civilObject["units"] = _settingsStore.Current.SpeckleUnits;

    // get basecurve
    List<ICurve> baseCurves = _baseCurveExtractor.GetBaseCurve(target);
    if (baseCurves.Count > 0)
    {
      civilObject["baseCurves"] = baseCurves;
    }

    // extract display value
    List<SOG.Mesh> display = _displayValueExtractor.GetDisplayValue(target);
    if (display.Count > 0)
    {
      civilObject["displayValue"] = display;
    }

    // POC: not setting property sets yet, need to determine connector parameter interoperability
    // POC: not setting part data yet, same reason as above


    return civilObject;
  }
}
