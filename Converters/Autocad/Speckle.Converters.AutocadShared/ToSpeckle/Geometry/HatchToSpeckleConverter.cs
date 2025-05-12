using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(ADB.Hatch), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Hatch, SOG.Region>
{
  private readonly ITypedConverter<ADB.Region, SOG.Region> _regionConverter;

  public HatchToSpeckleConverter(ITypedConverter<ADB.Region, SOG.Region> regionConverter)
  {
    _regionConverter = regionConverter;
  }

  public Base Convert(object target) => Convert((ADB.Hatch)target);

  public SOG.Region Convert(ADB.Hatch target)
  {
    ADB.Region? regionToConvert = null;

    for (int i = 0; i < target.NumberOfLoops; i++)
    {
      // Create 3d polyline from the HatchLoop
      ADB.HatchLoop loop = target.GetLoopAt(i);
      ADB.Curve polyline = PolylineFromLoop(loop);
      ADB.DBObjectCollection objCollection = new();
      objCollection.Add(polyline);

      // Convert polyline into an individual Region
      using (ADB.DBObjectCollection regionCollection = ADB.Region.CreateFromCurves(objCollection))
      {
        if (regionCollection.Count != 1)
        {
          throw new ConversionException(
            $"Hatch conversion failed {target}: unexpected number of regions generated from 1 hatch loop"
          );
        }
        ADB.Region loopRegion = (ADB.Region)regionCollection[0];

        // Assign first loop as the main Region, other Regions will be subtracted from it
        if (i == 0)
        {
          regionToConvert = loopRegion;
        }
        else
        {
          if (regionToConvert == null)
          {
            throw new ConversionException($"Hatch conversion failed: {target}");
          }
          // subtract region from Boundary region
          double areaBefore = regionToConvert.Area;
          regionToConvert.BooleanOperation(ADB.BooleanOperationType.BoolSubtract, loopRegion);

          // check if the region did not change after subtraction: means the loop was a separate hatch part
          if (Math.Abs(areaBefore - regionToConvert.Area) < 0.00001)
          {
            throw new ConversionException($"Composite hatches are not supported: {target}");
          }
        }
      }
    }

    if (regionToConvert == null)
    {
      throw new ConversionException($"Hatch conversion failed: {target}");
    }

    // convert and store Regions
    SOG.Region convertedRegion = _regionConverter.Convert(regionToConvert);
    convertedRegion.hasHatchPattern = true;

    return convertedRegion;
  }

  private ADB.Curve PolylineFromLoop(ADB.HatchLoop loop)
  {
    // collect vertices and construct a polyline simultaneously
    ADB.Polyline polyline = new() { Closed = true };
    int count = 0;

    // disposable object, wrapping into "using"
    using (AG.Point3dCollection vertices = new())
    {
      if (loop.IsPolyline)
      {
        foreach (ADB.BulgeVertex bVertex in loop.Polyline)
        {
          // don't add the end point that's the same as the start point
          AG.Point3d newPt = new(bVertex.Vertex.X, bVertex.Vertex.Y, 0);
          if (count == 0 || vertices[0].DistanceTo(newPt) > 0.00001)
          {
            vertices.Add(newPt);
            polyline.AddVertexAt(count, bVertex.Vertex, bVertex.Bulge, 0, 0);
            count++;
          }
        }

        // if only 2 points, that's a circle
        if (vertices.Count == 2)
        {
          AG.Point3d centerPt =
            new(
              vertices[0].X + (vertices[1].X - vertices[0].X) / 2,
              vertices[0].Y + (vertices[1].Y - vertices[0].Y) / 2,
              0
            );
          return new ADB.Circle(centerPt, AG.Vector3d.ZAxis, vertices[0].DistanceTo(vertices[1]) / 2);
        }
      }
      else
      {
        foreach (var segment in loop.Curves)
        {
          // for each segment, skip the last point: it will be added as a start Point of the next segment. Otherwise, they will overlap and make the curve invalid
          switch (segment)
          {
            case AG.LineSegment2d line:
              count = TryAddPointToPolyline(vertices, polyline, line.StartPoint, 0, count);
              break;

            case AG.NurbCurve2d nurb:
              // if there is only 1 loop curve, just return its Spline representation
              if (loop.Curves.Count == 1)
              {
                return Nurb2dToSpline(nurb);
              }
              // if there are more loop curves, approximate the nurb by splitting into segments
              // and making an arc from each for better fitting

              // estimate the number of points to split the nurb into, minimum 20
              double paramRange = nurb.EndParameter - nurb.StartParameter;
              int pointNumber = (int)(paramRange) >= 20 ? (int)(paramRange) : 20;
              for (double i = nurb.StartParameter; i < nurb.EndParameter; i += paramRange / pointNumber)
              {
                AG.Point2d pointStart = nurb.EvaluatePoint(i);
                AG.Point2d pointEnd = nurb.EvaluatePoint(i + paramRange / pointNumber);
                double bulgeNurb = GetVertexBulge(nurb, pointStart, pointEnd);

                count = TryAddPointToPolyline(vertices, polyline, pointStart, bulgeNurb, count);
              }
              break;

            case AG.CircularArc2d arc:
              // check if it's a circle
              if (loop.Curves.Count == 1 && Math.Abs(arc.EndAngle - arc.StartAngle) - 2 * Math.PI < 0.0001)
              {
                AG.Point3d centerPt = new(arc.Center.X, arc.Center.Y, 0);
                return new ADB.Circle(centerPt, AG.Vector3d.ZAxis, arc.Radius);
              }
              // if not a circle, add start point of the arc to the Polyline
              double bulge = Math.Tan((arc.EndAngle - arc.StartAngle) / 4);
              count = TryAddPointToPolyline(vertices, polyline, arc.StartPoint, bulge, count);
              break;

            case AG.EllipticalArc2d ellipse:
              // check if it's an ellipse
              if (loop.Curves.Count == 1 && Math.Abs(ellipse.EndAngle - ellipse.StartAngle) - 2 * Math.PI < 0.0001)
              {
                return new ADB.Ellipse(
                  new(ellipse.Center.X, ellipse.Center.Y, 0),
                  AG.Vector3d.ZAxis,
                  new AG.Vector3d(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, 0),
                  ellipse.MinorRadius / ellipse.MajorRadius,
                  ellipse.StartAngle,
                  ellipse.EndAngle
                );
              }
              throw new ConversionException($"Hatch segments of type {segment.GetType()} are not supported");
            default:
              throw new ConversionException($"Hatch segments of type {segment.GetType()} are not supported");
          }
        }
      }

      return polyline;
    }
  }

  private double GetVertexBulge(AG.NurbCurve2d nurb, AG.Point2d start, AG.Point2d end)
  {
    // get arc angle - assuming the arc is the curve between 2 points
    AG.Point2d midSegmentPoint = new(start.X + (end.X - start.X) / 2, start.Y + (end.Y - start.Y) / 2);
    AG.Point2d arcMidPoint = nurb.GetClosestPointTo(midSegmentPoint).Point;
    var adjacentSide = arcMidPoint.GetDistanceTo(midSegmentPoint);
    var hypotenuse = arcMidPoint.GetDistanceTo(start);
    var angleTriangle = Math.Acos(adjacentSide / hypotenuse);
    double arcAngle = 2 * (Math.PI - 2 * angleTriangle);

    // bulge direction: (-) clockwise, (+) counterclockwise
    int bulgeDirection =
      (end.X - start.X) * (arcMidPoint.Y - start.Y) - (arcMidPoint.X - start.X) * (end.Y - start.Y) > 0 ? -1 : 1;

    return Math.Tan(arcAngle / 4) * bulgeDirection;
  }

  private ADB.Spline Nurb2dToSpline(AG.NurbCurve2d nurb)
  {
    // get control points
    AG.Point3dCollection pointCollection = new();
    for (int i = 0; i < nurb.NumControlPoints; i++)
    {
      AG.Point2d pt = nurb.GetControlPointAt(i);
      pointCollection.Add(new AG.Point3d(pt.X, pt.Y, 0));
    }
    // get knots
    AG.DoubleCollection knotsCollection = new();
    foreach (double nurbKnot in nurb.Knots)
    {
      knotsCollection.Add(nurbKnot);
    }
    // get weights
    AG.DoubleCollection weightCollection = new();
    for (int i = 0; i < nurb.NumWeights; i++)
    {
      weightCollection.Add(nurb.GetWeightAt(i));
    }

    return new ADB.Spline(
      nurb.Degree,
      nurb.IsRational,
      nurb.IsClosed(),
      nurb.IsPeriodic(out _),
      pointCollection,
      knotsCollection,
      weightCollection,
      0,
      0
    );
  }

  private int TryAddPointToPolyline(
    AG.Point3dCollection vertices,
    ADB.Polyline polyline,
    AG.Point2d pointToAdd,
    double bulge,
    int count
  )
  {
    AG.Point3d point3d = new(pointToAdd.X, pointToAdd.Y, 0);
    // add point only if it doesn't overlap with the first one
    if (vertices.Count == 0 || vertices[0].DistanceTo(point3d) > 0.001)
    {
      vertices.Add(point3d);
      polyline.AddVertexAt(count, pointToAdd, bulge, 0, 0);
      return count + 1;
    }

    return count;
  }
}
