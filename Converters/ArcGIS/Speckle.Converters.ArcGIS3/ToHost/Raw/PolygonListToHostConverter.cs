using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

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
      throw new SpeckleConversionException("Feature contains no geometries");
    }
    List<ACG.Polygon> polyList = new();
    foreach (SGIS.PolygonGeometry poly in target)
    {
      ACG.Polyline? boundary = _polylineConverter.Convert(poly.boundary);

      // enforce clockwise outer ring orientation: https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic72904.html
      if (!boundary.IsClockwisePolygon())
      {
        boundary = ACG.GeometryEngine.Instance.ReverseOrientation(boundary) as ACG.Polyline;
      }

      if (boundary is null)
      {
        throw new SpeckleConversionException("Hatch conversion of boundary curve failed");
      }

      ACG.PolygonBuilderEx polyOuterRing = new(boundary.Parts.SelectMany(x => x), ACG.AttributeFlags.HasZ);

      // adding inner loops: https://github.com/esri/arcgis-pro-sdk/wiki/ProSnippets-Geometry#build-a-donut-polygon
      foreach (SOG.Polyline loop in poly.voids)
      {
        ACG.Polyline? loopNative = _polylineConverter.Convert(loop);

        // enforce clockwise outer ring orientation
        if (loopNative.IsClockwisePolygon())
        {
          loopNative = ACG.GeometryEngine.Instance.ReverseOrientation(loopNative) as ACG.Polyline;
        }

        if (loopNative is null)
        {
          throw new SpeckleConversionException("Hatch conversion of inner loop failed");
        }

        polyOuterRing.AddPart(loopNative.Copy3DCoordinatesToList());
      }
      ACG.Polygon polygon = polyOuterRing.ToGeometry();
      polyList.Add(polygon);
    }
    return new ACG.PolygonBuilderEx(polyList, ACG.AttributeFlags.HasZ).ToGeometry();
  }
}
