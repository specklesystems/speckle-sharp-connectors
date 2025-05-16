using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class CurveConverterToHost : ITypedConverter<SOG.Curve, DB.Curve>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;
  private readonly ITypedConverter<SOG.Polyline, DB.CurveArray> _polylineConverter;

  public CurveConverterToHost(
    ITypedConverter<SOG.Point, DB.XYZ> pointConverter,
    ITypedConverter<SOG.Polyline, DB.CurveArray> polylineConverter
  )
  {
    _pointConverter = pointConverter;
    _polylineConverter = polylineConverter;
  }

  public DB.Curve Convert(SOG.Curve target)
  {
    var pts = new List<DB.XYZ>();
    for (int i = 0; i < target.points.Count; i += 3)
    {
      //use PointToNative for conversion as that takes into account the Project Base Point
      var point = new SOG.Point(target.points[i], target.points[i + 1], target.points[i + 2], target.units);
      pts.Add(_pointConverter.Convert(point));
    }

    if (target.knots != null && target.weights != null && target.knots.Count > 0 && target.weights.Count > 0)
    {
      var weights = target.weights.GetRange(0, pts.Count);
      var speckleKnots = new List<double>(target.knots);
      if (speckleKnots.Count != pts.Count + target.degree + 1)
      {
        // Curve has rhino knots, repeat first and last.
        speckleKnots.Insert(0, speckleKnots[0]);
        speckleKnots.Add(speckleKnots[^1]);
      }

      try
      {
        //var knots = speckleKnots.GetRange(0, pts.Count + speckleCurve.degree + 1);
        var curve = DB.NurbSpline.CreateCurve(target.degree, speckleKnots, pts, weights);
        return curve;
      }
      // An exception was thrown by NurbSpline.CreateCurve
      // because Revit is stricter than Rhino regarding the input parameters for NURBS curves.
      // Exception message:
      // "The multiplicities of other interior knots must be at most degree - 2."
      // The solution below falls back to using displayValue.
      catch (Autodesk.Revit.Exceptions.ArgumentException)
      {
        var curveArray = _polylineConverter.Convert(target.displayValue);

        List<DB.XYZ> points = new List<DB.XYZ>();
        if (curveArray.Size > 0)
        {
          points.Add(curveArray.get_Item(0).GetEndPoint(0));

          foreach (DB.Curve curve in curveArray)
          {
            points.Add(curve.GetEndPoint(1));
          }
        }

        return DB.HermiteSpline.Create(points, false);
      }
    }
    else
    {
      var weights = target.weights.NotNull().GetRange(0, pts.Count);
      var curve = DB.NurbSpline.CreateCurve(pts, weights);
      return curve;
    }
  }
}
