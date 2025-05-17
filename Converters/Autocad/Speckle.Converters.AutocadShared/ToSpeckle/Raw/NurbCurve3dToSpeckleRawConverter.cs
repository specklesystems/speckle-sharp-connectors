using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class NurbCurve3dToSpeckleConverter : ITypedConverter<AG.NurbCurve3d, SOG.Curve>
{
  private readonly ITypedConverter<ADB.Spline, SOG.Curve> _splineConverter;

  public NurbCurve3dToSpeckleConverter(ITypedConverter<ADB.Spline, SOG.Curve> splineConverter)
  {
    _splineConverter = splineConverter;
  }

  public SOG.Curve Convert(AG.NurbCurve3d target)
  {
    // ADB.Spline is the closest representation of the AG.NurbCurve2d or 3d. We can construct a Spline and use Speckle splineConverter
    AG.DoubleCollection knotsCollection = new();
    AG.Point3dCollection pts = new();

    target.Knots.Cast<double>().ToList().ForEach(x => knotsCollection.Add(x));
    target.DefinitionData.ControlPoints.Cast<AG.Point3d>().ToList().ForEach(x => pts.Add(x));

    return _splineConverter.Convert(
      new ADB.Spline(
        target.Degree,
        target.IsRational,
        target.IsClosed(),
        target.IsPeriodic(out _),
        pts,
        knotsCollection,
        target.DefinitionData.Weights,
        0,
        0
      )
    );
  }
}
