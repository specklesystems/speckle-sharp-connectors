using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
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
    ADB.DBObjectCollection objCollection = new();
    for (int i = 0; i < target.NumberOfLoops; i++)
    {
      ADB.HatchLoop loop = target.GetLoopAt(i);

      ADB.Polyline3d polyline = PolylineFromLoop(loop);
      objCollection.Add(polyline);
    }

    // Create Regions from every curve. They are NOT ordered (i.e. first doesn't mean external)
    using (ADB.DBObjectCollection regionCollection = ADB.Region.CreateFromCurves(objCollection))
    {
      if (regionCollection.Count > 1)
      {
        for (int i = 1; i < regionCollection.Count; i++)
        {
          if (i > 0)
          {
            // throw new ConversionException("Composite Hatches are not supported");
          }
          ADB.Region innerRegion = (ADB.Region)regionCollection[i];
          // substract region from Boundary region
          //((ADB.Region)regionCollection[0]).BooleanOperation(ADB.BooleanOperationType.BoolSubtract, innerRegion);
          innerRegion.Dispose();
        }
      }

      ADB.Region adbRegion = (ADB.Region)regionCollection[0];

      // convert and store Regions
      SOG.Region convertedRegion = _regionConverter.Convert(adbRegion);
      convertedRegion.hasHatchPattern = true;

      return convertedRegion;
    }
  }

  // calculates bulge direction: (-) clockwise, (+) counterclockwise
  private int BulgeDirection(AG.Point2d start, AG.Point2d mid, AG.Point2d end)
  {
    // get vectors from points
    double[] v1 = new double[] { end.X - start.X, end.Y - start.Y, 0 }; // vector from start to end point
    double[] v2 = new double[] { mid.X - start.X, mid.Y - start.Y, 0 }; // vector from start to mid point

    // calculate cross product z direction
    double z = v1[0] * v2[1] - v2[0] * v1[1];

    return z > 0 ? -1 : 1;
  }

  private ADB.Polyline3d PolylineFromLoop(ADB.HatchLoop loop)
  {
    // initialize loop Polyline
    AG.Point3dCollection vertices = new();
    //ADB.Polyline polyline = new() { Closed = true };

    int count = 0;
    if (loop.IsPolyline)
    {
      foreach (ADB.BulgeVertex bVertex in loop.Polyline)
      {
        // last point will repeat the first, ignore it
        if (count < loop.Polyline.Count - 1)
        {
          //polyline.AddVertexAt(count, bVertex.Vertex.Y, bVertex.Bulge, 0, 0);
          vertices.Add(new AG.Point3d(bVertex.Vertex.X, bVertex.Vertex.Y, 0));
          count++;
        }
      }
    }
    else
    {
      foreach (var loopCurve in loop.Curves)
      {
        switch (loopCurve)
        {
          case AG.LineSegment2d line:
            //polyline.AddVertexAt(count, line.StartPoint, 0, 0, 0);
            vertices.Add(new AG.Point3d(line.StartPoint.X, line.StartPoint.Y, 0));
            //count++;
            break;
          case AG.CircularArc2d arc:
            AG.Point2d midPoint = arc.EvaluatePoint(arc.StartAngle + (arc.EndAngle - arc.StartAngle) / 2);
            double measure =
              (2 * Math.Asin(arc.StartPoint.GetDistanceTo(midPoint) / (2 * arc.Radius)))
              + (2 * Math.Asin(midPoint.GetDistanceTo(arc.EndPoint) / (2 * arc.Radius)));
            if (measure <= 0 || measure >= 2 * Math.PI)
            {
              throw new ArgumentOutOfRangeException(nameof(loop), "Cannot convert arc with measure <= 0 or >= 2 pi");
            }

            //var bulge = Math.Tan(measure / 4) * BulgeDirection(arc.StartPoint, midPoint, arc.EndPoint);
            //polyline.AddVertexAt(count, arc.StartPoint, 0, 0, 0);
            vertices.Add(new AG.Point3d(arc.StartPoint.X, arc.StartPoint.Y, 0));
            count++;
            //polyline.AddVertexAt(count, midPoint, bulge, 0, 0);
            vertices.Add(new AG.Point3d(midPoint.X, midPoint.Y, 0));
            count++;
            break;
        }
      }
    }
    var polyline = new ADB.Polyline3d(ADB.Poly3dType.SimplePoly, vertices, true);
    return polyline;
  }
}
