using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc3dToSpeckleConverter(
  ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
  ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<AG.CircularArc3d, SOG.Arc>
{
  public Result<SOG.Arc> Convert(AG.CircularArc3d target)
  {
    if (!planeConverter.Try(new(target.Center, target.Normal), out Result<SOG.Plane> plane))
    {
      return plane.Failure<SOG.Arc>();
    }
    if (!pointConverter.Try(target.StartPoint, out Result<SOG.Point> start))
    {
      return start.Failure<SOG.Arc>();
    }
    if (!pointConverter.Try(target.EndPoint, out Result<SOG.Point> end))
    {
      return start.Failure<SOG.Arc>();
    }
    double startParam = target.GetParameterOf(target.StartPoint);
    double endParam = target.GetParameterOf(target.EndPoint);
    AG.Point3d midPoint = target.EvaluatePoint(target.StartAngle + (target.EndAngle - target.StartAngle) / 2);
    if (!pointConverter.Try(midPoint, out Result<SOG.Point> mid))
    {
      return start.Failure<SOG.Arc>();
    }
    SOG.Arc arc =
      new()
      {
        plane = plane.Value,
        startPoint = start.Value,
        endPoint = end.Value,
        midPoint = mid.Value,
        domain = new SOP.Interval { start = startParam, end = endParam },
        units = settingsStore.Current.SpeckleUnits,
      };

    return Success(arc);
  }
}
