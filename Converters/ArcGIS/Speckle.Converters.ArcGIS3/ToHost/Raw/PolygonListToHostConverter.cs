using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class PolygonListToHostConverter : ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon>
{
  private readonly ITypedConverter<SOG.Polyline, ACG.Polyline> _polylineConverter;

  public PolygonListToHostConverter(ITypedConverter<SOG.Polyline, ACG.Polyline> polylineConverter)
  {
    _polylineConverter = polylineConverter;
  }

  public ACG.Polygon Convert(List<SGIS.PolygonGeometry> target)
  {
    if (target.Count == 0)
    {
      throw new ValidationException("Feature contains no geometries");
    }
    List<ACG.Polygon> polyList = new();
    foreach (SGIS.PolygonGeometry poly in target)
    {
      ACG.Polyline boundary = _polylineConverter.Convert(poly.boundary);
      ACG.PolygonBuilderEx polyOuterRing = new(boundary);

      foreach (SOG.Polyline loop in poly.voids)
      {
        // adding inner loops: https://github.com/esri/arcgis-pro-sdk/wiki/ProSnippets-Geometry#build-a-donut-polygon
        ACG.Polyline loopNative = _polylineConverter.Convert(loop);
        polyOuterRing.AddPart(loopNative.Copy3DCoordinatesToList());
      }
      ACG.Polygon polygon = polyOuterRing.ToGeometry();
      polyList.Add(polygon);
    }
    return new ACG.PolygonBuilderEx(polyList, ACG.AttributeFlags.HasZ).ToGeometry();
  }
}
