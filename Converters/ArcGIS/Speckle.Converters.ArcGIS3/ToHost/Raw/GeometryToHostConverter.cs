using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class GeometryToHostConverter : ITypedConverter<IReadOnlyList<Base>, ACG.Geometry>
{
  private readonly ITypedConverter<List<SOG.Polyline>, ACG.Polyline> _polylineConverter;
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _multipointConverter;
  private readonly ITypedConverter<List<SGIS.PolygonGeometry3d>, ACG.Multipatch> _polygon3dConverter;
  private readonly ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon> _polygonConverter;
  private readonly ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch> _multipatchConverter;

  public GeometryToHostConverter(
    ITypedConverter<List<SOG.Polyline>, ACG.Polyline> polylineConverter,
    ITypedConverter<List<SOG.Point>, ACG.Multipoint> multipointConverter,
    ITypedConverter<List<SGIS.PolygonGeometry3d>, ACG.Multipatch> polygon3dConverter,
    ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon> polygonConverter,
    ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch> multipatchConverter
  )
  {
    _polylineConverter = polylineConverter;
    _multipointConverter = multipointConverter;
    _polygon3dConverter = polygon3dConverter;
    _polygonConverter = polygonConverter;
    _multipatchConverter = multipatchConverter;
  }

  public ACG.Geometry Convert(IReadOnlyList<Base> target)
  {
    if (target.Count == 0)
    {
      throw new ValidationException("Feature contains no geometry");
    }

    return target[0] switch
    {
      SOG.Point => _multipointConverter.Convert(target.Cast<SOG.Point>().ToList()),
      SOG.Polyline => _polylineConverter.Convert(target.Cast<SOG.Polyline>().ToList()),
      SGIS.PolygonGeometry3d => _polygon3dConverter.Convert(target.Cast<SGIS.PolygonGeometry3d>().ToList()),
      SGIS.PolygonGeometry => _polygonConverter.Convert(target.Cast<SGIS.PolygonGeometry>().ToList()),
      SGIS.GisMultipatchGeometry => _multipatchConverter.Convert(target.Cast<SGIS.GisMultipatchGeometry>().ToList()),
      _ => throw new ValidationException($"No conversion found for type {target[0]}")
    };
  }
}
