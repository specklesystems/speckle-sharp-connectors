using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class GeometryToHostConverter : ITypedConverter<IReadOnlyList<Base>, ACG.Geometry>
{
  private readonly ITypedConverter<List<SOG.Polyline>, ACG.Polyline> _polylineConverter;
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _multipointConverter;

  public GeometryToHostConverter(
    ITypedConverter<List<SOG.Polyline>, ACG.Polyline> polylineConverter,
    ITypedConverter<List<SOG.Point>, ACG.Multipoint> multipointConverter
  )
  {
    _polylineConverter = polylineConverter;
    _multipointConverter = multipointConverter;
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
      _ => throw new ValidationException($"No conversion found for type {target[0]}")
    };
  }
}
