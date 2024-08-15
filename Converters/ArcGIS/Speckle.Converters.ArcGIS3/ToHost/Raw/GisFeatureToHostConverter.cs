using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class GisFeatureToHostConverter : ITypedConverter<GisFeature, (ACG.Geometry?, Dictionary<string, object?>)>
{
  private readonly ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> _geometryConverter;

  public GisFeatureToHostConverter(ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> geometryConverter)
  {
    _geometryConverter = geometryConverter;
  }

  public (ACG.Geometry?, Dictionary<string, object?>) Convert(GisFeature target)
  {
    Dictionary<string, object?> attributes = target.attributes.GetMembers(DynamicBaseMemberType.Dynamic);

    if (target.geometry is List<Base> geometry)
    {
      ACG.Geometry nativeShape = _geometryConverter.Convert(geometry);
      return (nativeShape, attributes);
    }
    else
    {
      return (null, attributes);
    }
  }
}
