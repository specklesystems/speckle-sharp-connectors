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
[NameAndRankValue(nameof(SGIS.GisPointFeature), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GisPointFeatureToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SGIS.GisPointFeature, (Base, ACG.Geometry?)>
{
  private readonly ITypedConverter<List<SOG.Point>, ACG.Multipoint> _multipointConverter;

  public GisPointFeatureToHostConverter(ITypedConverter<List<SOG.Point>, ACG.Multipoint> multipointConverter)
  {
    _multipointConverter = multipointConverter;
  }

  public object Convert(Base target) => Convert((SGIS.GisPointFeature)target);

  public (Base, ACG.Geometry?) Convert(SGIS.GisPointFeature target)
  {
    ACG.Multipoint multipoint = _multipointConverter.Convert(target.geometry);
    return (target, multipoint);
  }
}
