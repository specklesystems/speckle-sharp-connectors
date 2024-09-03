using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc3dToSpeckleConverter : ITypedConverter<AG.CircularArc3d, SOG.Arc>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public CircularArc3dToSpeckleConverter(
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
    SOG.Plane plane = _planeConverter.Convert(target.GetPlane());
    SOG.Point start = _pointConverter.Convert(target.StartPoint);
    SOG.Point end = _pointConverter.Convert(target.EndPoint);
    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point3d midPoint = target.EvaluatePoint(endParam - startParam / 2.0);

    // some circular arcs will **not** return a correct value from `EvaluatePoint` using the indicated parameter at the midpoint.
    // so far, this has happened with some arc segments in the polyline method. They will have an end param > 1, and evaluatePoint returns the endpoint
    // this is why we are checking for midpoint == endpoint, and using a [0,1] parameterization if this is the case.
    if (midPoint.IsEqualTo(target.EndPoint))
    {
      midPoint = target.EvaluatePoint(0.5);
    }

    SOG.Point mid = _pointConverter.Convert(midPoint);

    SOG.Arc arc =
      new(
        plane,
        target.Radius,
        target.StartAngle,
        target.EndAngle,
        target.EndAngle - target.StartAngle, // POC: testing, unsure
        _settingsStore.Current.SpeckleUnits
      )
      {
        startPoint = start,
        endPoint = end,
        midPoint = mid,
        domain = new SOP.Interval { start = startParam, end = endParam },
        length = target.GetLength(0, 1, 0.000)
      };

    return arc;
  }
}
