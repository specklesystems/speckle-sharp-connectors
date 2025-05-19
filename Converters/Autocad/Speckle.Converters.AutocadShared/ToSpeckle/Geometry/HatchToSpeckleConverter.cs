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
    // keep track of the vertices added to 'polyline', because to add new points we need to insert them via specific index (in method TryAddPointToPolyline)
    int count = 0;

    // disposable object, wrapping into "using"
    using (AG.Point3dCollection vertices = new())
    {
      if (loop.IsPolyline)
      {
        foreach (ADB.BulgeVertex bVertex in loop.Polyline)
        {
          count = TryAddPointToPolyline(vertices, polyline, bVertex.Vertex, bVertex.Bulge, count);
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
        return polyline;
      }

      // if .Polyline is null, read from .Curves
      if (loop.Curves.Count == 0)
      {
        throw new ConversionException($"Hatch loop doesn't contain any segments.");
      }

      if (loop.Curves.Count > 1) // handle the multi-segment case only with Line segments (not able to produce a Hatch Loop with multiple segments of other types)
      {
        foreach (var lineSegment in loop.Curves)
        {
          // for each segment, skip the last point: it will be added as a start Point of the next segment. Otherwise, they will overlap and make the curve invalid
          count = lineSegment is AG.LineSegment2d line
            ? TryAddPointToPolyline(vertices, polyline, line.StartPoint, 0, count)
            : throw new ConversionException($"Hatch segments of type {lineSegment.GetType()} are not supported");
        }
        return polyline;
      }

      var segment = loop.Curves[0]; // if .Curve has only 1 segments, it can be a closed Circle, Ellipse or Nurb
      switch (segment)
      {
        case AG.CircularArc2d arc:
          if (Math.Abs(arc.EndAngle - arc.StartAngle) - 2 * Math.PI > 0.0001) // check if it's not a circle
          {
            throw new ConversionException($"Multiple hatch segments of type {segment.GetType()} are not supported");
          }
          return new ADB.Circle(new(arc.Center.X, arc.Center.Y, 0), AG.Vector3d.ZAxis, arc.Radius);

        case AG.EllipticalArc2d ellipse:
          if (Math.Abs(ellipse.EndAngle - ellipse.StartAngle) - 2 * Math.PI > 0.0001) // check if it's not an ellipse
          {
            throw new ConversionException($"Multiple hatch segments of type {segment.GetType()} are not supported");
          }
          return new ADB.Ellipse(
            new(ellipse.Center.X, ellipse.Center.Y, 0),
            AG.Vector3d.ZAxis,
            new AG.Vector3d(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, 0),
            ellipse.MinorRadius / ellipse.MajorRadius,
            ellipse.StartAngle,
            ellipse.EndAngle
          );

        case AG.NurbCurve2d nurb:

          AG.DoubleCollection knotsCollection = new();
          AG.Point3dCollection pts = new();

          nurb.Knots.Cast<double>().ToList().ForEach(x => knotsCollection.Add(x));
          nurb.DefinitionData.ControlPoints.Cast<AG.Point2d>()
            .ToList()
            .ForEach(x => pts.Add(new AG.Point3d(x.X, x.Y, 0.0)));

          return new ADB.Spline(
            nurb.Degree,
            nurb.IsRational,
            nurb.IsClosed(),
            nurb.IsPeriodic(out _),
            pts,
            knotsCollection,
            nurb.DefinitionData.Weights,
            0,
            0
          );
        default:
          throw new ConversionException($"Multiple hatch segments of type {segment.GetType()} are not supported");
      }
    }
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
