using Autodesk.Revit.DB;
using Speckle.Objects.Geometry;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.RevitShared.ToHost.Raw;

public class SurfaceConverterToHost : ITypedConverter<SOG.Surface, DB.BRepBuilderSurfaceGeometry>
{
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;

  public SurfaceConverterToHost(ITypedConverter<SOG.Point, XYZ> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public BRepBuilderSurfaceGeometry Convert(SOG.Surface target)
  {
    var uvBox = new DB.BoundingBoxUV(target.knotsU[0], target.knotsV[0], target.knotsU[^1], target.knotsV[^1]);
    var surfPts = target.GetControlPoints();
    var uKnots = SurfaceKnotsToNative(target.knotsU);
    var vKnots = SurfaceKnotsToNative(target.knotsV);
    var cPts = ControlPointsToNative(surfPts);

    BRepBuilderSurfaceGeometry result;
    if (!target.rational)
    {
      result = BRepBuilderSurfaceGeometry.CreateNURBSSurface(
        target.degreeU,
        target.degreeV,
        uKnots,
        vKnots,
        cPts,
        false,
        uvBox
      );
    }
    else
    {
      var weights = ControlPointWeightsToNative(surfPts);
      result = BRepBuilderSurfaceGeometry.CreateNURBSSurface(
        target.degreeU,
        target.degreeV,
        uKnots,
        vKnots,
        cPts,
        weights,
        false,
        uvBox
      );
    }

    return result;
  }

  private double[] SurfaceKnotsToNative(List<double> list)
  {
    var count = list.Count;
    var knots = new double[count + 2];

    int j = 0,
      k = 0;
    while (j < count)
    {
      knots[++k] = list[j++];
    }

    knots[0] = knots[1];
    knots[count + 1] = knots[count];

    return knots;
  }

  public XYZ[] ControlPointsToNative(List<List<ControlPoint>> controlPoints)
  {
    var uCount = controlPoints.Count;
    var vCount = controlPoints[0].Count;
    var count = uCount * vCount;
    var points = new DB.XYZ[count];
    int p = 0;

    controlPoints.ForEach(row =>
      row.ForEach(pt =>
      {
        var point = new SOG.Point(pt.x, pt.y, pt.z, pt.units);
        points[p++] = _pointConverter.Convert(point);
      })
    );

    return points;
  }

  public double[] ControlPointWeightsToNative(List<List<ControlPoint>> controlPoints)
  {
    var uCount = controlPoints.Count;
    var vCount = controlPoints[0].Count;
    var count = uCount * vCount;
    var weights = new double[count];
    int p = 0;

    controlPoints.ForEach(row => row.ForEach(pt => weights[p++] = pt.weight));

    return weights;
  }
}
