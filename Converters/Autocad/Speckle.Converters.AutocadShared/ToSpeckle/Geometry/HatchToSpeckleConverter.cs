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

  /// <summary>
  /// Converting AutoCAD Hatch to Speckle Region. This method first converts Hatch to AutoCAD Region, and then uses RegionToSpeckle converter.
  /// This is done because AutoCAD Region class allows to directly extract Brep representation (and convert it to display Mesh),
  /// get distinct Outer and Inner loops, and filter out Hatches that consist of multiple disconnected parts (which our Region class is not designed for).
  /// </summary>
  /// <param name="target">AutoCAD Hatch object</param>
  /// <returns>Speckle Region with property 'hasHatchPattern' as 'true'</returns>
  public SOG.Region Convert(ADB.Hatch target)
  {
    ADB.Region? regionToConvert = null;

    for (int i = 0; i < target.NumberOfLoops; i++)
    {
      // Convert HatchLoop into DBObjectCollection for the subsequent construction of the Region (.CreateFromCurves())
      ADB.HatchLoop loop = target.GetLoopAt(i);
      List<ADB.Curve> polyline = ConvertHatchLoopToCurveEntityList(loop);
      ADB.DBObjectCollection objCollection = new();
      polyline.ForEach(x => objCollection.Add(x));

      // Convert a loop (represented by DBObjectCollection) into an individual Region
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

  /// <summary>
  /// Converting AutoCAD HatchLoop into a list of AutoCAD Curves, so they can be used to construct an AutoCAD Region.
  /// </summary>
  private List<ADB.Curve> ConvertHatchLoopToCurveEntityList(ADB.HatchLoop loop)
  {
    List<ADB.Curve> curveList = new();

    // disposable object, wrapping into "using"
    using (AG.Point3dCollection pts = new())
    {
      if (loop.IsPolyline)
      {
        // loop.Polyline doesn't actually return a Polyline, but a 'BulgeVertexCollection'. We need to construct a Polyline from it
        ADB.Polyline polyline = new() { Closed = true };

        // keep track of the number of vertices added to 'polyline', because to add new points we need to insert them via specific index (in method .AddVertexAt())
        for (int i = 0; i < loop.Polyline.Count; i++)
        {
          ADB.BulgeVertex bVertex = loop.Polyline[i];
          AG.Point3d point3d = new(bVertex.Vertex.X, bVertex.Vertex.Y, 0);

          // add point only if it doesn't overlap with the first one ('closed' property is already set). Otherwise, polyline will be invalid.
          if (pts.Count == 0 || pts[0].DistanceTo(point3d) > 0.001)
          {
            pts.Add(new AG.Point3d(bVertex.Vertex.X, bVertex.Vertex.Y, 0));
            polyline.AddVertexAt(i, bVertex.Vertex, bVertex.Bulge, 0, 0);
          }
        }

        // if only 2 points, that's a circle
        if (loop.Polyline.Count == 2)
        {
          AG.Point3d centerPt = new(pts[0].X + (pts[1].X - pts[0].X) / 2, pts[0].Y + (pts[1].Y - pts[0].Y) / 2, 0);
          curveList.Add(new ADB.Circle(centerPt, AG.Vector3d.ZAxis, pts[0].DistanceTo(pts[1]) / 2));
          return curveList;
        }

        curveList.Add(polyline);
        return curveList;
      }
    }

    // if .Polyline is null, read from .Curves
    if (loop.Curves.Count == 0)
    {
      throw new ConversionException($"Hatch loop doesn't contain any segments.");
    }

    if (loop.Curves.Count > 1) // handle the multi-segment case only with Line segments (not able to produce a Hatch Loop with multiple segments of other types)
    {
      foreach (AG.Curve2d curve2d in loop.Curves)
      {
        if (curve2d is not AG.LineSegment2d l)
        {
          throw new ConversionException($"Hatch segments of type {curve2d.GetType()} are not supported");
        }

        ADB.Line line =
          new(new AG.Point3d(l.StartPoint.X, l.StartPoint.Y, 0), new AG.Point3d(l.EndPoint.X, l.EndPoint.Y, 0));
        curveList.Add(line);
      }
      return curveList;
    }

    var segment = loop.Curves[0]; // if .Curve has only 1 segments, it can be a closed Circle, Ellipse or Nurb
    switch (segment)
    {
      case AG.CircularArc2d arc:
        if (Math.Abs(arc.EndAngle - arc.StartAngle) - 2 * Math.PI > 0.0001) // check if it's not a circle
        {
          throw new ConversionException($"Multiple hatch segments of type {segment.GetType()} are not supported");
        }

        curveList.Add(new ADB.Circle(new(arc.Center.X, arc.Center.Y, 0), AG.Vector3d.ZAxis, arc.Radius));
        return curveList;

      case AG.EllipticalArc2d ellipse:
        if (Math.Abs(ellipse.EndAngle - ellipse.StartAngle) - 2 * Math.PI > 0.0001) // check if it's not an ellipse
        {
          throw new ConversionException($"Multiple hatch segments of type {segment.GetType()} are not supported");
        }
        curveList.Add(
          new ADB.Ellipse(
            new(ellipse.Center.X, ellipse.Center.Y, 0),
            AG.Vector3d.ZAxis,
            new AG.Vector3d(ellipse.MajorAxis.X, ellipse.MajorAxis.Y, 0),
            ellipse.MinorRadius / ellipse.MajorRadius,
            ellipse.StartAngle,
            ellipse.EndAngle
          )
        );
        return curveList;

      case AG.NurbCurve2d nurb:
        using (AG.Point3dCollection ptsNurb = new())
        {
          AG.DoubleCollection knotsCollection = new();

          nurb.Knots.Cast<double>().ToList().ForEach(x => knotsCollection.Add(x));
          nurb.DefinitionData.ControlPoints.Cast<AG.Point2d>()
            .ToList()
            .ForEach(x => ptsNurb.Add(new AG.Point3d(x.X, x.Y, 0.0)));
          curveList.Add(
            new ADB.Spline(
              nurb.Degree,
              nurb.IsRational,
              nurb.IsClosed(),
              nurb.IsPeriodic(out _),
              ptsNurb,
              knotsCollection,
              nurb.DefinitionData.Weights,
              0,
              0
            )
          );
          return curveList;
        }
      default:
        throw new ConversionException($"Multiple hatch segments of type {segment.GetType()} are not supported");
    }
  }
}
