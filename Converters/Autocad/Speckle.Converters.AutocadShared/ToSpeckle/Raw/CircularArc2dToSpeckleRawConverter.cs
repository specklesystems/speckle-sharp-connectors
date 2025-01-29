using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc2dToSpeckleConverter(
  ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<AG.CircularArc2d, SOG.Arc>
{
  public Result<SOG.Arc> Convert(AG.CircularArc2d target)
  {
    string units = settingsStore.Current.SpeckleUnits;

    // find arc plane (normal is in counterclockwise dir)
    var center3 = new AG.Point3d(target.Center.X, target.Center.Y, 0);
    AG.Plane plane = target.IsClockWise
      ? new AG.Plane(center3, AG.Vector3d.ZAxis.MultiplyBy(-1))
      : new AG.Plane(center3, AG.Vector3d.ZAxis);

    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point2d midPoint = target.EvaluatePoint(target.StartAngle + (target.EndAngle - target.StartAngle) / 2);

    if (!planeConverter.Try(plane, out var planeResult))
    {
      return planeResult.Failure<SOG.Arc>();
    }
    // create arc
    var arc = new SOG.Arc()
    {
      plane = planeResult.Value,
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
      domain = new SOP.Interval { start = startParam, end = endParam },
      units = units
    };

    return Success(arc);
  }
}
