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
[NameAndRankValue(nameof(SGIS.GisPolylineFeature), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class GisPolylineFeatureToHostConverter
  : IToHostTopLevelConverter,
    ITypedConverter<SGIS.GisPolylineFeature, (Base, ACG.Geometry?)>
{
  private readonly ITypedConverter<List<SOG.Polyline>, ACG.Polyline> _polylineConverter;

  public GisPolylineFeatureToHostConverter(ITypedConverter<List<SOG.Polyline>, ACG.Polyline> polylineConverter)
  {
    _polylineConverter = polylineConverter;
  }

  public object Convert(Base target) => Convert((SGIS.GisPolylineFeature)target);

  public (Base, ACG.Geometry?) Convert(SGIS.GisPolylineFeature target)
  {
    ACG.Polyline polyline = _polylineConverter.Convert(target.geometry);
    return (target, polyline);
  }
}
