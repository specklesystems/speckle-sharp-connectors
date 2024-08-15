using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

/// <summary>
/// Converter for <see cref="GisFeature"/> (which is sent by QGIS V2) with geometry.
/// </summary>
/// <exception cref="ArgumentException"> Thrown when GisFeature has null or empty geometry</exception>
public class GisFeatureToHostConverter : ITypedConverter<GisFeature, (ACG.Geometry, Dictionary<string, object?>)>
{
  private readonly ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> _geometryConverter;

  public GisFeatureToHostConverter(ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> geometryConverter)
  {
    _geometryConverter = geometryConverter;
  }

  public (ACG.Geometry, Dictionary<string, object?>) Convert(GisFeature target)
  {
    Dictionary<string, object?> attributes = target.attributes.GetMembers(DynamicBaseMemberType.Dynamic);

    if (target.geometry is List<Base> geometry)
    {
      ACG.Geometry nativeShape = _geometryConverter.Convert(geometry);
      return (nativeShape, attributes);
    }
    else
    {
      throw new ArgumentException("GisFeature had null or empty geometry");
    }
  }
}
