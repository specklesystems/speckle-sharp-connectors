using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class IGisFeatureToHostConverter : ITypedConverter<IGisFeature, (ACG.Geometry?, Dictionary<string, object?>)>
{
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _multipointConverter;
  private readonly ITypedConverter<List<SOG.Polyline>, ACG.Polyline> _polylineConverter;
  private readonly ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon> _polygonConverter;
  private readonly ITypedConverter<List<SGIS.PolygonGeometry3d>, ACG.Multipatch> _polygon3dConverter;
  private readonly ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch> _multipatchConverter;

  public IGisFeatureToHostConverter(
    ITypedConverter<List<SOG.Point>, ACG.Multipoint> multipointConverter,
    ITypedConverter<List<SOG.Polyline>, ACG.Polyline> polylineConverter,
    ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon> polygonConverter,
    ITypedConverter<List<SGIS.PolygonGeometry3d>, ACG.Multipatch> polygon3dConverter,
    ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch> multipatchConverter
  )
  {
    _multipointConverter = multipointConverter;
    _polylineConverter = polylineConverter;
    _polygonConverter = polygonConverter;
    _polygon3dConverter = polygon3dConverter;
    _multipatchConverter = multipatchConverter;
  }

  public (ACG.Geometry?, Dictionary<string, object?>) Convert(IGisFeature target)
  {
    // get attributes
    Dictionary<string, object?> attributes = target.attributes.GetMembers(DynamicBaseMemberType.Dynamic);

    switch (target)
    {
      case GisNonGeometricFeature:
        return (null, attributes);

      case GisPointFeature pointFeature:
        ACG.Multipoint multipoint = _multipointConverter.Convert(pointFeature.geometry);
        return (multipoint, attributes);

      case GisPolylineFeature polylineFeature:
        ACG.Polyline polyline = _polylineConverter.Convert(polylineFeature.geometry);
        return (polyline, attributes);

      case GisPolygonFeature polygonFeature:
        ACG.Polygon polygon = _polygonConverter.Convert(polygonFeature.geometry);
        return (polygon, attributes);

      case GisMultipatchFeature multipatchFeature:
        List<SGIS.PolygonGeometry3d> polygonGeometry = multipatchFeature
          .geometry.Where(o => o is SGIS.PolygonGeometry3d)
          .Cast<SGIS.PolygonGeometry3d>()
          .ToList();
        List<SGIS.GisMultipatchGeometry> multipatchGeometry = multipatchFeature
          .geometry.Where(o => o is SGIS.GisMultipatchGeometry)
          .Cast<SGIS.GisMultipatchGeometry>()
          .ToList();
        ACG.Multipatch? multipatch =
          polygonGeometry.Count > 0
            ? _polygon3dConverter.Convert(polygonGeometry)
            : multipatchGeometry.Count > 0
              ? _multipatchConverter.Convert(multipatchGeometry)
              : null;
        if (multipatch is null)
        {
          throw new ArgumentException(
            "Multipatch geometry contained no valid types (PolygonGeometry3d or GisMultipatchGeometry"
          );
        }

        return (multipatch, attributes);

      default:
        throw new NotSupportedException($"{target.GetType} is not supported.");
    }
  }
}
