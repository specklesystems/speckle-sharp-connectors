using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class GeometryToHostConverter : ITypedConverter<IReadOnlyList<Base>, ACG.Geometry>
{
  private readonly ITypedConverter<List<SOG.Polyline>, ACG.Polyline> _polylineConverter;
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _multipointConverter;
  private readonly ITypedConverter<List<SOG.Polygon>, ACG.Polygon> _polygonConverter;

  public GeometryToHostConverter(
    ITypedConverter<List<SOG.Polyline>, ACG.Polyline> polylineConverter,
    ITypedConverter<List<SOG.Point>, ACG.Multipoint> multipointConverter,
    ITypedConverter<List<SOG.Polygon>, ACG.Polygon> polygonConverter
  )
  {
    _polylineConverter = polylineConverter;
    _multipointConverter = multipointConverter;
    _polygonConverter = polygonConverter;
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
      SOG.Polygon => _polygonConverter.Convert(target.Cast<SOG.Polygon>().ToList()),
      _ => throw new ValidationException($"No conversion found for type {target[0]}")
    };
  }
}
