using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

/// <summary>
/// Converter for <see cref="IGisFeature"/> with geometry.
/// </summary>
/// <exception cref="ArgumentException"> Thrown when IGisFeature is <see cref="GisNonGeometricFeature"/> because it has no geometry, or when Multipatch geometry contained invalid types.</exception>
/// <exception cref="NotSupportedException">Thrown for unsupported <see cref="IGisFeature"/> classes.</exception>
public class IGisFeatureToHostConverter : ITypedConverter<IGisFeature, (Base, ACG.Geometry)>
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

  public (Base, ACG.Geometry) Convert(IGisFeature target)
  {
    switch (target)
    {
      case GisNonGeometricFeature:
        throw new ArgumentException("IGisFeature had null or empty geometry");

      case GisPointFeature pointFeature:
        ACG.Multipoint multipoint = _multipointConverter.Convert(pointFeature.geometry);
        return (pointFeature, multipoint);

      case GisPolylineFeature polylineFeature:
        ACG.Polyline polyline = _polylineConverter.Convert(polylineFeature.geometry);
        return (polylineFeature, polyline);

      case GisPolygonFeature polygonFeature:
        ACG.Polygon polygon = _polygonConverter.Convert(polygonFeature.geometry);
        return (polygonFeature, polygon);

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

        return (multipatchFeature, multipatch);

      default:
        throw new NotSupportedException($"{target.GetType} is not supported.");
    }
  }
}
