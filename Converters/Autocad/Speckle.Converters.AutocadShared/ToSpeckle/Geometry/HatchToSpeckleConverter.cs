using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
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
    // generate Mesh for displayValue by converting to Regions first
    List<SOG.Region> regions = new();
    List<SOG.Mesh> displayValue = new();

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
      if (regionCollection.Count != 1)
      {
        throw new ConversionException("Composite Hatches are not supported");
      }

      foreach (var region in regionCollection)
      {
        if (region is ADB.Region adbRegion)
        {
          using ABR.Brep brep = new(adbRegion);
          if (brep.IsNull)
          {
            throw new ConversionException("Could not retrieve brep from the hatch.");
          }
          // convert and store Meshes
          SOG.Mesh mesh = _brepConverter.Convert(brep);
          mesh.area = adbRegion.Area;
          displayValue.Add(mesh);

          // convert and store Regions
          SOG.Region convertedRegion = _regionConverter.Convert(adbRegion);
          convertedRegion.hasHatchPattern = true;
          regions.Add(convertedRegion);
        }
      }
    }

    return regions[0];
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

    if (loop.IsPolyline)
    {
      int count = 0;
      foreach (ADB.BulgeVertex bVertex in loop.Polyline)
      {
        polyline.AddVertexAt(count, bVertex.Vertex, bVertex.Bulge, 0, 0);
      }
    }
    else
    {
      int count = 0;
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
            polyline.AddVertexAt(count, arc.StartPoint, bulge, 0, 0);
            count++;
            break;
        }
      }
    }

    return polyline;
  }
}
