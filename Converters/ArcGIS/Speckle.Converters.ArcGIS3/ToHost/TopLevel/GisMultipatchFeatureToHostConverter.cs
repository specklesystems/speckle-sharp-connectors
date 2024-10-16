using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

/// <summary>
/// Converter for <see cref="IGisFeature"/> with geometry.
/// </summary>
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
    if (target.geometry.Count == 0)
    {
      throw new ArgumentException("Multipatch Feature contains no geometries");
    }

    ACG.Multipatch? multipatch;
    try
    {
      multipatch = _multipatchConverter.Convert(target.geometry.Cast<SGIS.GisMultipatchGeometry>().ToList());
    }
    catch (InvalidCastException)
    {
      multipatch = _polygon3dConverter.Convert(target.geometry.Cast<SGIS.PolygonGeometry3d>().ToList());
    }

    if (multipatch is null)
    {
      throw new ConversionException("Multipatch conversion did not return valid geometry");
    }

    return multipatch;
  }
}
