using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

public class SegmentCollectionToSpeckleConverter : ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline>
{
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;

  public SegmentCollectionToSpeckleConverter(
    IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack,
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter
  )
  {
    _contextStack = contextStack;
    _pointConverter = pointConverter;
  }

  public SOG.Polyline Convert(ACG.ReadOnlySegmentCollection target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic8480.html
    double len = 0;

    List<SOG.Point> points = new();
    foreach (var segment in target)
    {
      len += segment.Length;

      if (segment.SegmentType != ACG.SegmentType.Line)
      {
        // densify the segments with curves using precision value of the Map's Spatial Reference
        ACG.Polyline polylineFromSegment = new ACG.PolylineBuilderEx(
          segment,
          ACG.AttributeFlags.HasZ,
          _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference
        ).ToGeometry();

        double tolerance = _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference.XYTolerance;
        double conversionFactorToMeter = _contextStack
          .Current
          .Document
          .ActiveCRSoffsetRotation
          .SpatialReference
          .Unit
          .ConversionFactor;
        var densifiedPolyline = ACG.GeometryEngine.Instance.DensifyByDeviation(
          polylineFromSegment,
          tolerance * conversionFactorToMeter
        );
        if (densifiedPolyline == null)
        {
          throw new ArgumentException("Segment densification failed");
        }

        ACG.Polyline polylineToConvert = (ACG.Polyline)densifiedPolyline;
        // add points from each segment of the densified original segment
        ACG.ReadOnlyPartCollection subParts = polylineToConvert.Parts;
        foreach (ACG.ReadOnlySegmentCollection subSegments in subParts)
        {
          foreach (ACG.Segment? subSegment in subSegments)
          {
            ACG.MapPoint startPt = new ACG.MapPointBuilderEx(
              subSegment.StartPoint.X,
              subSegment.StartPoint.Y,
              subSegment.StartPoint.Z,
              target.SpatialReference
            ).ToGeometry();
            ACG.MapPoint endPt = new ACG.MapPointBuilderEx(
              subSegment.EndPoint.X,
              subSegment.EndPoint.Y,
              subSegment.EndPoint.Z,
              target.SpatialReference
            ).ToGeometry();

            AddPtsToPolylinePts(
              points,
              new List<SOG.Point>() { _pointConverter.Convert(startPt), _pointConverter.Convert(endPt) }
            );
          }
        }
      }
      else
      {
        AddPtsToPolylinePts(
          points,
          new List<SOG.Point>()
          {
            _pointConverter.Convert(segment.StartPoint),
            _pointConverter.Convert(segment.EndPoint)
          }
        );
      }
    }

    // check the last point, remove if coincides with the first. Assign as Closed instead
    bool closed = false;
    if (
      Math.Round(points[^1].x, 6) == Math.Round(points[0].x, 6)
      && Math.Round(points[^1].y, 6) == Math.Round(points[0].y, 6)
      && Math.Round(points[^1].z, 6) == Math.Round(points[0].z, 6)
    )
    {
      closed = true;
      points.RemoveAt(points.Count - 1);
    }

    SOG.Polyline polyline =
      new()
      {
        value = points.SelectMany(pt => new[] { pt.x, pt.y, pt.z, }).ToList(),
        closed = closed,
        units = _contextStack.Current.SpeckleUnits
      };

    return polyline;
  }

  private List<SOG.Point> AddPtsToPolylinePts(List<SOG.Point> points, List<SOG.Point> newSegmentPts)
  {
    // don't add the same Point as the previous one
    if (
      points.Count == 0
      || Math.Round(points[^1].x, 6) != Math.Round(newSegmentPts[0].x, 6)
      || Math.Round(points[^1].y, 6) != Math.Round(newSegmentPts[0].y, 6)
      || Math.Round(points[^1].z, 6) != Math.Round(newSegmentPts[0].z, 6)
    )
    {
      points.AddRange(newSegmentPts);
    }
    else
    {
      points.AddRange(newSegmentPts.GetRange(1, newSegmentPts.Count - 1));
    }
    return points;
  }
}
