using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(ADB.Hatch), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Hatch, SOG.Region>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;
  private readonly ITypedConverter<ADB.Region, SOG.Region> _regionConverter;

  public HatchToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
    ITypedConverter<ADB.Region, SOG.Region> regionConverter
  )
  {
    _brepConverter = brepConverter;
    _regionConverter = regionConverter;
  }

  public Base Convert(object target) => Convert((ADB.Hatch)target);

  public SOG.Region Convert(ADB.Hatch target)
  {
    ADB.DBObjectCollection objCollection = new();
    // target.Explode method is failing in runtime. e.g. target.Explode(objCollection);
    for (int i = 0; i < target.NumberOfLoops; i++)
    {
      ADB.HatchLoop loop = target.GetLoopAt(i);
      ADB.Polyline polyline = PolylineFromLoop(loop);
      objCollection.Add(polyline);
    }

    using (ADB.DBObjectCollection regionCollection = ADB.Region.CreateFromCurves(objCollection))
    {
      if (regionCollection.Count > 1)
      {
        for (int i = 1; i < regionCollection.Count; i++)
        {
          ADB.Region innerRegion = (ADB.Region)regionCollection[i];
          // substract region from Boundary region
          ((ADB.Region)regionCollection[0]).BooleanOperation(ADB.BooleanOperationType.BoolSubtract, innerRegion);
          innerRegion.Dispose();
        }
      }

      //if (regionCollection.Count != 1)
      //{
      //  throw new ConversionException("Composite Hatches are not supported");
      //}
      ADB.Region adbRegion = (ADB.Region)regionCollection[0];
      /*
      using ABR.Brep brep = new(adbRegion);
      if (brep.IsNull)
      {
        throw new ConversionException("Could not retrieve brep from the hatch.");
      }
      // convert and store Meshes
      SOG.Mesh mesh = _brepConverter.Convert(brep);
      mesh.area = adbRegion.Area;
      displayValue.Add(mesh);
      */

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

  private ADB.Polyline PolylineFromLoop(ADB.HatchLoop loop)
  {
    // initialize loop Polyline
    ADB.Polyline polyline = new() { Closed = true };
    int count = 0;
    if (loop.IsPolyline)
    {
      foreach (ADB.BulgeVertex bVertex in loop.Polyline)
      {
        polyline.AddVertexAt(count, bVertex.Vertex, bVertex.Bulge, 0, 0);
        count++;
      }
    }
    else
    {
      foreach (var loopCurve in loop.Curves)
      {
        switch (loopCurve)
        {
          case AG.LineSegment2d line:
            polyline.AddVertexAt(count, line.StartPoint, 0, 0, 0);
            count++;
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

            var bulge = Math.Tan(measure / 4) * BulgeDirection(arc.StartPoint, midPoint, arc.EndPoint);
            polyline.AddVertexAt(count, arc.StartPoint, 0, 0, 0);
            count++;
            polyline.AddVertexAt(count, midPoint, bulge, 0, 0);
            count++;
            break;
        }
      }
    }

    return polyline;
  }
}
