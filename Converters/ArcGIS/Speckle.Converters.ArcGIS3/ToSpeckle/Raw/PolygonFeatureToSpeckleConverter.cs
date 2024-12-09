using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Raw;

/// <summary>
/// Converts a Polygon feature to a list of polylines from the polygon boundary and inner loops.
/// This is a placeholder conversion since we don't have a polygon class or meshing strategy for interior loops yet.
/// </summary>
public class PolygonFeatureToSpeckleConverter : ITypedConverter<ACG.Polygon, IReadOnlyList<SOG.Polyline>>
{
  private readonly ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> _segmentConverter;

  public PolygonFeatureToSpeckleConverter(ITypedConverter<ACG.ReadOnlySegmentCollection, SOG.Polyline> segmentConverter)
  {
    _segmentConverter = segmentConverter;
  }

  public IReadOnlyList<SOG.Polyline> Convert(ACG.Polygon target)
  {
    // https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic30235.html
    int partCount = target.PartCount;
    List<SOG.Polyline> parts = new(partCount);

    if (partCount == 0)
    {
      throw new ValidationException("ArcGIS Polygon contains no parts");
    }

    for (int i = 0; i < partCount; i++)
    {
      // get the part polyline
      ACG.ReadOnlySegmentCollection segmentCollection = target.Parts[i];
      SOG.Polyline polyline = _segmentConverter.Convert(segmentCollection);
      // POC: we could create a mesh from exterior polyline: target.IsExteriorRing(idx)
      parts.Add(polyline);
    }

    return parts;
  }
}
