using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;
using AECPropDB = Autodesk.Aec.PropertyData.DatabaseServices;

namespace Speckle.Converters.Civil3d.ToSpeckle.BuiltElements;

[NameAndRankValue(nameof(CDB.Entity), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class CivilEntityToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<ADB.Curve, Objects.ICurve> _curveConverter;
  private readonly ITypedConverter<ADB.Solid3d, SOG.Mesh> _solidConverter;
  private readonly ITypedConverter<AECPropDB.PropertySet, List<DataField>> _propertySetConverter;
  private readonly IConverterSettingsStore<Civil3dConversionSettings> _settingsStore;

  public CivilEntityToSpeckleTopLevelConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<ADB.Curve, Objects.ICurve> curveConverter,
    ITypedConverter<ADB.Solid3d, SOG.Mesh> solidConverter,
    ITypedConverter<AECPropDB.PropertySet, List<DataField>> propertySetConverter,
    IConverterSettingsStore<Civil3dConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _curveConverter = curveConverter;
    _solidConverter = solidConverter;
    _propertySetConverter = propertySetConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((CDB.Entity)target);

  public Base Convert(CDB.Entity target)
  {
    ICurve curve = _curveConverter.Convert(target.BaseCurve);

    Base civilObject = new();
    civilObject["category"] = target.GetRXClass().AppName;
    civilObject["name"] = target.Name;
    civilObject["baseCurve"] = curve;
    civilObject["units"] = _settingsStore.Current.SpeckleUnits;

    if (target is CDB.Part part)
    {
      // can get solid body info from part for display value
      SOG.Mesh display = _solidConverter.Convert(part.Solid3dBody);
      civilObject["displayValue"] = display;
    }

    // POC: not setting property sets yet, need to determine connector parameter interoperability
    // POC: not setting part data yet, same reason as above

    return civilObject;
  }
}
