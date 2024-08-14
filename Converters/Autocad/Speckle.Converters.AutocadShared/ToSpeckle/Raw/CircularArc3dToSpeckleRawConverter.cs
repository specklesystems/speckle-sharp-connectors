using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class CircularArc3dToSpeckleConverter : ITypedConverter<AG.CircularArc3d, SOG.Arc>
{
  private readonly ITypedConverter<AG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<AG.Plane, SOG.Plane> _planeConverter;
  private readonly IConversionContextStack<Document, ADB.UnitsValue> _contextStack;

  public CircularArc3dToSpeckleConverter(
    ITypedConverter<AG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<AG.Plane, SOG.Plane> planeConverter,
    IConversionContextStack<Document, ADB.UnitsValue> contextStack
  )
  {
    _pointConverter = pointConverter;
    _planeConverter = planeConverter;
    _contextStack = contextStack;
  }

  public SOG.Arc Convert(AG.CircularArc3d target)
  {
    SOG.Plane plane = _planeConverter.Convert(target.GetPlane());
    SOG.Point start = _pointConverter.Convert(target.StartPoint);
    SOG.Point end = _pointConverter.Convert(target.EndPoint);
    double domainUpper = target.GetInterval().UpperBound;
    double domainLower = target.GetInterval().LowerBound;
    SOG.Point mid = _pointConverter.Convert(target.EvaluatePoint(domainUpper - domainLower / 2.0));

    SOG.Arc arc =
      new(
        plane,
        target.Radius,
        target.StartAngle,
        target.EndAngle,
        target.EndAngle - target.StartAngle, // POC: testing, unsure
        _contextStack.Current.SpeckleUnits
      )
      {
        startPoint = start,
        endPoint = end,
        midPoint = mid,
        domain = new SOP.Interval { start = domainLower, end = domainUpper },
        length = target.GetLength(0, 1, 0.000)
      };

    return arc;
  }
}
