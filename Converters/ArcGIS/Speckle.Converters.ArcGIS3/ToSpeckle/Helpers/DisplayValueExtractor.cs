using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.Helpers;

public sealed class DisplayValueExtractor
{
  private readonly ITypedConverter<ACG.MapPoint, SOG.Point> _pointConverter;
  private readonly ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> _multiPointConverter;
  private readonly ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> _polylineConverter;
  private readonly ITypedConverter<ACG.Polygon, List<Base>> _polygonConverter;
  private readonly ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> _multipatchConverter;
  private readonly ITypedConverter<ACD.Raster.Raster, SOG.Mesh> _gisRasterConverter;

  public DisplayValueExtractor(
    ITypedConverter<ACG.MapPoint, SOG.Point> pointConverter,
    ITypedConverter<ACG.Multipoint, IReadOnlyList<SOG.Point>> multiPointConverter,
    ITypedConverter<ACG.Polyline, IReadOnlyList<SOG.Polyline>> polylineConverter,
    ITypedConverter<ACG.Polygon, List<Base>> polygonConverter,
    ITypedConverter<ACG.Multipatch, IReadOnlyList<Base>> multipatchConverter,
    ITypedConverter<ACD.Raster.Raster, SOG.Mesh> gisRasterConverter
  )
  {
    _pointConverter = pointConverter;
    _multiPointConverter = multiPointConverter;
    _polylineConverter = polylineConverter;
    _polygonConverter = polygonConverter;
    _multipatchConverter = multipatchConverter;
    _gisRasterConverter = gisRasterConverter;
  }

  public IEnumerable<Base> GetDisplayValue(AC.CoreObjectsBase coreObjectsBase)
  {
    switch (coreObjectsBase)
    {
      case ACD.Row row:
        foreach (Base shape in GetRowGeometries(row))
        {
          yield return shape;
        }
        break;

      case ACD.Raster.Raster raster:
        yield return _gisRasterConverter.Convert(raster);
        break;

      case ACD.Analyst3D.LasPoint point:
        break;

      default:
        yield break;
    }
  }

  private IEnumerable<Base> GetRowGeometries(ACD.Row row)
  {
    // see if this row contains any geometry fields
    // POC: is it possible to have multiple geometry fields in a row?
    string? geometryField = row.GetFields()
      .Where(o => o.FieldType == ACD.FieldType.Geometry)
      ?.Select(o => o.Name)
      ?.First();

    if (geometryField is null)
    {
      yield break;
    }

    var shape = (ACG.Geometry)row[geometryField];
    switch (shape)
    {
      case ACG.MapPoint point:
        yield return _pointConverter.Convert(point);
        break;

      case ACG.Multipoint multipoint:
        foreach (SOG.Point converted in _multiPointConverter.Convert(multipoint))
        {
          yield return converted;
        }
        break;

      case ACG.Polyline polyline:
        foreach (SOG.Polyline converted in _polylineConverter.Convert(polyline))
        {
          yield return converted;
        }
        break;

      case ACG.Polygon polygon:
        foreach (Base converted in _polygonConverter.Convert(polygon))
        {
          yield return converted;
        }
        break;

      case ACG.Multipatch multipatch:
        foreach (Base converted in _multipatchConverter.Convert(multipatch))
        {
          yield return converted;
        }
        break;

      default:
        throw new ValidationException($"No geometry conversion found for {shape.GetType().Name}");
    }
  }
}
