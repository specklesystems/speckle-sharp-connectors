using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class PolygonListToHostConverter : ITypedConverter<List<SOG.Polygon>, ACG.Polygon>
{
  private readonly ITypedConverter<SOG.Polyline, ACG.Polyline> _polylineConverter;

  public PolygonListToHostConverter(ITypedConverter<SOG.Polyline, ACG.Polyline> polylineConverter)
  {
    _polylineConverter = polylineConverter;
  }

  public ACG.Polygon Convert(List<SOG.Polygon> target)
  {
    if (target.Count == 0)
    {
      throw new ValidationException("Feature contains no geometries");
    }
    List<ACG.Polygon> polyList = new();
    foreach (SOG.Polygon poly in target)
    {
      if (poly.boundary is SOG.Polyline boundaryPolyline)
      {
        ACG.Polyline boundary = _polylineConverter.Convert(boundaryPolyline);
        ACG.PolygonBuilderEx polyOuterRing = new(boundary);

        foreach (ICurve loop in poly.voids)
        {
          if (loop is SOG.Polyline loopPolyline)
          {
            // adding inner loops: https://github.com/esri/arcgis-pro-sdk/wiki/ProSnippets-Geometry#build-a-donut-polygon
            ACG.Polyline loopNative = _polylineConverter.Convert(loopPolyline);
            polyOuterRing.AddPart(loopNative.Copy3DCoordinatesToList());
          }
        }
        ACG.Polygon polygon = polyOuterRing.ToGeometry();
        polyList.Add(polygon);
      }
      // TODO: add the case for ICurve boundaries
    }
    return new ACG.PolygonBuilderEx(polyList, ACG.AttributeFlags.HasZ).ToGeometry();
  }
}
