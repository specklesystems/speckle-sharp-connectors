using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

/// <summary>
/// Converter for <see cref="IGisFeature"/> with geometry.
/// </summary>
/// <exception cref="ArgumentException"> Thrown when IGisFeature is <see cref="SGIS.GisNonGeometricFeature"/> because it has no geometry, or when Multipatch geometry contained invalid types.</exception>
/// <exception cref="NotSupportedException">Thrown for unsupported <see cref="IGisFeature"/> classes.</exception>
[NameAndRankValue(nameof(SGIS.GisPolygonFeature), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GisPolygonFeatureToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SGIS.GisPolygonFeature, (Base, ACG.Geometry?)>
{
  private readonly ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon> _polygonConverter;

  public GisPolygonFeatureToHostConverter(ITypedConverter<List<SGIS.PolygonGeometry>, ACG.Polygon> polygonConverter)
  {
    _polygonConverter = polygonConverter;
  }

  public object Convert(Base target) => Convert((SGIS.GisPolygonFeature)target);

  public (Base, ACG.Geometry?) Convert(SGIS.GisPolygonFeature target)
  {
    ACG.Polygon polygon = _polygonConverter.Convert(target.geometry);
    return (target, polygon);
  }
}
