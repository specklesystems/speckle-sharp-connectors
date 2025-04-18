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
    if (loop.IsPolyline)
    {
      // disposable object, wrapping into "using"
      using (AG.Point3dCollection vertices = new())
      {
        // collect vertices and construct a polyline simultaneously, it will be clear what to use after iterating
        ADB.Polyline polyline = new() { Closed = true };

        int count = 0;
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
        return polyline;
      }
    }

    throw new ConversionException("Hatch loop conversion failed.");
  }
}
