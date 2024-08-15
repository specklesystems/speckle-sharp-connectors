using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

/// <summary>
/// This is a utility converter to handle compatibility with the old GisFeature class, used by V2 QGIS.
/// </summary>
/// <remarks>Can be removed if we decided to not support V2 QGIS to V3 ArcGIS</remarks>
public class BaseGisFeatureToHostConverter
  : ITypedConverter<List<Base>, List<(ACG.Geometry?, Dictionary<string, object?>)>>
{
  private readonly ITypedConverter<GisFeature, (ACG.Geometry?, Dictionary<string, object?>)> _gisFeatureConverter;
  private readonly ITypedConverter<IGisFeature, (ACG.Geometry?, Dictionary<string, object?>)> _iGisFeatureConverter;

  public BaseGisFeatureToHostConverter(
    ITypedConverter<GisFeature, (ACG.Geometry?, Dictionary<string, object?>)> gisFeatureConverter,
    ITypedConverter<IGisFeature, (ACG.Geometry?, Dictionary<string, object?>)> iGisFeatureConverter
  )
  {
    _gisFeatureConverter = gisFeatureConverter;
    _iGisFeatureConverter = iGisFeatureConverter;
  }

  public List<(ACG.Geometry?, Dictionary<string, object?>)> Convert(List<Base> target)
  {
    List<(ACG.Geometry?, Dictionary<string, object?>)> featureClassElements = new();

    List<IGisFeature> gisFeatures = target.Where(o => o is IGisFeature).Cast<IGisFeature>().ToList();
    if (gisFeatures.Count > 0)
    {
      featureClassElements = gisFeatures.Select(o => _iGisFeatureConverter.Convert(o)).ToList();
    }
    else // V2 compatibility with QGIS (still using GisFeature class)
    {
      List<GisFeature> oldGisFeatures = target.Where(o => o is GisFeature).Cast<GisFeature>().ToList();
      featureClassElements = oldGisFeatures.Select(o => _gisFeatureConverter.Convert(o)).ToList();
    }
    return featureClassElements;
  }
}
