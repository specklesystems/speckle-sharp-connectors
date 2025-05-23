using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc3dToSpeckleRawConverter : ITypedConverter<AG.CircularArc3d, SOG.Arc>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public CircularArc3dToSpeckleRawConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Arc Convert(AG.CircularArc3d target)
  {
    SOG.Plane plane = _planeConverter.Convert(new(target.Center, target.Normal));
    SOG.Point start = _pointConverter.Convert(target.StartPoint);
    SOG.Point end = _pointConverter.Convert(target.EndPoint);
    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point3d midPoint = target.EvaluatePoint(target.StartAngle + (target.EndAngle - target.StartAngle) / 2);
    SOG.Point mid = _pointConverter.Convert(midPoint);

    SOG.Arc arc =
      new()
      {
        plane = plane,
        startPoint = start,
        endPoint = end,
        midPoint = mid,
        domain = new SOP.Interval { start = startParam, end = endParam },
        units = _settingsStore.Current.SpeckleUnits,
      };

    return arc;
  }
}
