using Speckle.Converters.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad.ToHost.Geometry;

[NameAndRankValue(typeof(SOG.Arc), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class ArcToHostConverter(ITypedConverter<SOG.Arc, AG.CircularArc3d> arcConverter)
  : ITypedConverter<SOG.Arc, ADB.Arc>
{
  public Result<ADB.Arc> Convert(SOG.Arc target)
  {
    // the most reliable method to convert to autocad convention is to calculate from start, end, and midpoint
    // because of different plane & start/end angle conventions
    if (!arcConverter.Try(target, out Result<AG.CircularArc3d> circularArc))
    {
      return circularArc.Failure<ADB.Arc>();
    }

    // calculate adjusted start and end angles from circularArc reference
    // for some reason, if just the circular arc start and end angle props are used, this moves the endpoints of the created arc
    // so we need to calculate the adjusted start and end angles from the circularArc reference vector.
    AG.Plane plane = new(circularArc.Value.Center, circularArc.Value.Normal);
    double angleOnPlane = circularArc.Value.ReferenceVector.AngleOnPlane(plane);
    double adjustedStartAngle = circularArc.Value.StartAngle + angleOnPlane;
    double adjustEndAngle = circularArc.Value.EndAngle + angleOnPlane;

    return Success(
      new ADB.Arc(
        circularArc.Value.Center,
        circularArc.Value.Normal,
        circularArc.Value.Radius,
        adjustedStartAngle,
        adjustEndAngle
      )
    );
  }
}
