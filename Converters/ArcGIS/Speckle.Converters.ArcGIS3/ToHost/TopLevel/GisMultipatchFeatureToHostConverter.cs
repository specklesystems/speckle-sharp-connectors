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
[NameAndRankValue(nameof(SGIS.GisMultipatchFeature), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GisMultipatchFeatureToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SGIS.GisMultipatchFeature, ACG.Geometry>
{
  private readonly ITypedConverter<List<SGIS.PolygonGeometry3d>, ACG.Multipatch> _polygon3dConverter;
  private readonly ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch> _multipatchConverter;

  public GisMultipatchFeatureToHostConverter(
    ITypedConverter<List<SGIS.PolygonGeometry3d>, ACG.Multipatch> polygon3dConverter,
    ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch> multipatchConverter
  )
  {
    _polygon3dConverter = polygon3dConverter;
    _multipatchConverter = multipatchConverter;
  }

  public object Convert(Base target) => Convert((SGIS.GisMultipatchFeature)target);

  public ACG.Geometry Convert(SGIS.GisMultipatchFeature target)
  {
    List<SGIS.PolygonGeometry3d> polygonGeometry = target
      .geometry.Where(o => o is SGIS.PolygonGeometry3d)
      .Cast<SGIS.PolygonGeometry3d>()
      .ToList();
    List<SGIS.GisMultipatchGeometry> multipatchGeometry = target
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
    return multipatch;
  }
}
