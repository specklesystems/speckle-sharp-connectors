using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBArcToSpeckleRawConverter : ITypedConverter<ADB.Arc, SOG.Arc>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly ITypedConverter<ADB.Extents3d, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBArcToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    ITypedConverter<ADB.Extents3d, SOG.Box> boxConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Arc)target);

  public SOG.Arc Convert(ADB.Arc target)
  {
    SOG.Plane plane = _planeConverter.Convert(new(target.Center, target.Normal));
    SOG.Point start = _pointConverter.Convert(target.StartPoint);
    SOG.Point end = _pointConverter.Convert(target.EndPoint);
    SOG.Point mid = _pointConverter.Convert(target.GetPointAtDist(target.Length / 2.0));
    SOP.Interval domain = new() { start = target.StartParam, end = target.EndParam };
    SOG.Box bbox = _boxConverter.Convert(target.GeometricExtents);

    SOG.Arc arc =
      new()
      {
        plane = plane,
        startPoint = start,
        endPoint = end,
        midPoint = mid,
        domain = domain,
        bbox = bbox,
        units = _settingsStore.Current.SpeckleUnits
      };

    return arc;
  }
}
