using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc2dToSpeckleRawConverter : ITypedConverter<AG.CircularArc2d, SOG.Arc>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public CircularArc2dToSpeckleRawConverter(
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _planeConverter = planeConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Arc Convert(AG.CircularArc2d target)
  {
    string units = _settingsStore.Current.SpeckleUnits;

    // find arc plane (normal is in counterclockwise dir)
    var center3 = new AG.Point3d(target.Center.X, target.Center.Y, 0);
    AG.Plane plane = target.IsClockWise
      ? new AG.Plane(center3, AG.Vector3d.ZAxis.MultiplyBy(-1))
      : new AG.Plane(center3, AG.Vector3d.ZAxis);

    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point2d midPoint = target.EvaluatePoint(target.StartAngle + (target.EndAngle - target.StartAngle) / 2);

    // create arc
    var arc = new SOG.Arc()
    {
      plane = _planeConverter.Convert(plane),
      startPoint = new()
      {
        x = target.StartPoint.X,
        y = target.StartPoint.Y,
        z = 0,
        units = units,
      },
      endPoint = new()
      {
        x = target.EndPoint.X,
        y = target.EndPoint.Y,
        z = 0,
        units = units,
      },
      midPoint = new()
      {
        x = midPoint.X,
        y = midPoint.Y,
        z = 0,
        units = units,
      },
      domain = new SOP.Interval { start = startParam, end = endParam },
      units = units,
    };

    return arc;
  }
}
