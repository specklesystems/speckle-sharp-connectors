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
  /// Converting AutoCAD Hatch to Speckle Region.
  /// This method first converts Hatch to AutoCAD Region, and then uses RegionToSpeckle converter.
  /// AutoCAD Region is a much simpler class than Hatch, and converting to region allows us to handle a bunch of unsupported conditions in Hatches.
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

    // move this region to the target elevation
    // POC: I've tried passing this elevation to ConvertHatchLoopToCurveEntityList() for direct assignment when converting 2d to 3d points, but this results in non-planarity in splines for some reason.
    regionToConvert.TransformBy(AG.Matrix3d.Displacement(new AG.Vector3d(0, 0, target.Elevation)));

    // convert and store Regions
    SOG.Region convertedRegion = _regionConverter.Convert(regionToConvert);
    convertedRegion.hasHatchPattern = true;

    return convertedRegion;
  }

  /// <summary>
  /// Converts Hatchloops to database-resident curve entities.
  /// Curve entities are required by the Region create method.
  /// </summary>
  private List<ADB.Curve> ConvertHatchLoopToCurveEntityList(ADB.HatchLoop loop)
  {
    List<ADB.Curve> curveList = new();

    // 1 - handle the case of a polyline first
    if (loop.IsPolyline)
    {
      // create a polyline from the loop.Polyline BulgeVertexCollection
      ADB.Polyline polyline = new() { Closed = true };

      for (int i = 0; i < loop.Polyline.Count; i++)
      {
        var vertex = loop.Polyline[i];

        // check if this is the last point, the closed property is already set and duplicated endpoints will result in an invalid polyline
        if (i == loop.Polyline.Count - 1 && vertex.Vertex.GetDistanceTo(loop.Polyline[0].Vertex) < 0.001)
        {
          continue;
        }

        polyline.AddVertexAt(i, vertex.Vertex, vertex.Bulge, 0, 0);
      }

      curveList.Add(polyline);
      return curveList;
    }

    // 2 - if the loop is not a polyline, handle the loop curves
    // Notes: empirically, it seems that whenever the curve count is 1, it is a closed curve type like circle, ellipse, etc
    // and when the curve count is > 1, they are line segments that will comprise of a closed area
    // We'll process curves accordingly
    if (loop.Curves.Count == 0)
    {
      throw new ConversionException($"Hatch loop doesn't contain any segments.");
    }

    foreach (AG.Curve2d curve in loop.Curves)
    {
      ADB.Curve? curveEntity = null;
      switch (curve)
      {
        case AG.LineSegment2d l:
          curveEntity = new ADB.Line(
            new AG.Point3d(l.StartPoint.X, l.StartPoint.Y, 0),
            new AG.Point3d(l.EndPoint.X, l.EndPoint.Y, 0)
          );
          break;

        case AG.CircularArc2d c:
          AG.Point3d cCenter = new(c.Center.X, c.Center.Y, 0);
          curveEntity =
            c.EndPoint == c.StartPoint
              ? new ADB.Circle(cCenter, AG.Vector3d.ZAxis, c.Radius)
              : new ADB.Arc(cCenter, c.Radius, c.StartAngle, c.EndAngle);
          break;

        case AG.EllipticalArc2d e:
          curveEntity = new ADB.Ellipse(
            new AG.Point3d(e.Center.X, e.Center.Y, 0),
            AG.Vector3d.ZAxis,
            new AG.Vector3d(e.MajorAxis.X, e.MajorAxis.Y, 0),
            e.MinorRadius / e.MajorRadius,
            e.StartAngle,
            e.EndAngle
          );
          break;

        case AG.NurbCurve2d n: // need to convert to spline, ew
          AG.Point3dCollection controlPoints = new();
          AG.DoubleCollection knots = new();
          n.Knots.Cast<double>().ToList().ForEach(x => knots.Add(x));
          n.DefinitionData.ControlPoints.Cast<AG.Point2d>()
            .ToList()
            .ForEach(x => controlPoints.Add(new AG.Point3d(x.X, x.Y, 0)));

          curveEntity = new ADB.Spline(
            n.Degree,
            n.IsRational,
            n.IsClosed(),
            n.IsPeriodic(out _),
            controlPoints,
            knots,
            n.DefinitionData.Weights,
            0,
            0
          );
          break;

        default:
          throw new ConversionException($"Segments of type {curve.GetType()} are not supported");
      }

      curveList.Add(curveEntity);
    }

    return curveList;
  }
}
