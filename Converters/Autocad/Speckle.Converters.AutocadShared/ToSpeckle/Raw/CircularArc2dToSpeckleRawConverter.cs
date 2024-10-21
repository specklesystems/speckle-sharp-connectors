using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc2dToSpeckleConverter : ITypedConverter<AG.CircularArc2d, SOG.Arc>
{
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public CircularArc2dToSpeckleConverter(
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

    // find arc plane (normal is in clockwise dir)
    var center3 = new AG.Point3d(target.Center.X, target.Center.Y, 0);
    AG.Plane plane = target.IsClockWise
      ? new AG.Plane(center3, AG.Vector3d.ZAxis.MultiplyBy(-1))
      : new AG.Plane(center3, AG.Vector3d.ZAxis);

    // calculate total angle. TODO: This needs to be validated across all possible arc orientations
    var totalAngle = target.IsClockWise
      ? Math.Abs(target.EndAngle - target.StartAngle)
      : Math.Abs(target.EndAngle - target.StartAngle);

    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point2d midPoint = target.EvaluatePoint(target.StartAngle + (target.EndAngle - target.StartAngle) / 2);

    // create arc
    var arc = new SOG.Arc()
    {
      plane = _planeConverter.Convert(plane),
      radius = target.Radius,
      startPoint = new()
      {
        x = target.StartPoint.X,
        y = target.StartPoint.Y,
        z = 0,
        units = units
      },
      endPoint = new()
      {
        x = target.EndPoint.X,
        y = target.EndPoint.Y,
        z = 0,
        units = units
      },
      midPoint = new()
      {
        x = midPoint.X,
        y = midPoint.Y,
        z = 0,
        units = units
      },
      startAngle = target.StartAngle,
      endAngle = target.EndAngle,
      angleRadians = totalAngle,
      domain = new SOP.Interval { start = startParam, end = endParam },
      length = target.GetLength(0, 1),
      units = units
    };

    return arc;
  }
}
