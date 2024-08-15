using ArcGIS.Core.Data;
using ArcGIS.Core.Internal.CIM;
using ArcGIS.Desktop.Internal.Mapping;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class GisFeatureToHostConverter : ITypedConverter<GisFeature, (ACG.Geometry, Dictionary<string, object?>)>
{
  private readonly ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> _geometryConverter;
  private readonly ITypedConverter<Base, Dictionary<string, object?>> _attributeConverter;

  public GisFeatureToHostConverter(
    ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> geometryConverter,
    ITypedConverter<Base, Dictionary<string, object?>> attributeConverter
  )
  {
    _geometryConverter = geometryConverter;
    _attributeConverter = attributeConverter;
  }

  public (ACG.Geometry, Dictionary<string, object?>) Convert(GisFeature target)
  {
    Dictionary<string, object?> attributes = _attributeConverter.Convert(target.attributes);
    if (target.geometry is List<Base> geometry)
    {
      ACG.Geometry nativeShape = _geometryConverter.Convert(geometry);
      return (nativeShape, attributes);
    }
    else
    {
      throw new SpeckleConversionException("Feature geometry was null or empty");
    }
  }
}
