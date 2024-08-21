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
[NameAndRankValue(nameof(SGIS.GisNonGeometricFeature), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GisNonGeometricFeatureToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SGIS.GisNonGeometricFeature, (Base, ACG.Geometry?)>
{
  public object Convert(Base target) => Convert((SGIS.GisNonGeometricFeature)target);

  public (Base, ACG.Geometry?) Convert(SGIS.GisNonGeometricFeature target)
  {
    return (target, null);
  }
}
