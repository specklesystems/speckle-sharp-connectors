using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(VectorLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class VectorLayerToHostConverter : IToHostTopLevelConverter, ITypedConverter<VectorLayer, object>
{
  private readonly ITypedConverter<IGisFeature, (Base, ACG.Geometry)> _iGisFeatureConverter;
  private readonly ITypedConverter<GisFeature, (Base, ACG.Geometry)> _gisFeatureConverter;
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public VectorLayerToHostConverter(
    ITypedConverter<IGisFeature, (Base, ACG.Geometry)> iGisFeatureConverter,
    ITypedConverter<GisFeature, (Base, ACG.Geometry)> gisFeatureConverter,
    IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack
  )
  {
    _iGisFeatureConverter = iGisFeatureConverter;
    _gisFeatureConverter = gisFeatureConverter;
    _contextStack = contextStack;
  }

  public object Convert(Base target) => Convert((VectorLayer)target);

  public object Convert(VectorLayer target)
  {
    SetCrsDataOnReceive(target);
    // handle and convert element types
    List<(Base, ACG.Geometry)> featureClassElements = new();

    List<IGisFeature> gisFeatures = target.elements.Where(o => o is IGisFeature).Cast<IGisFeature>().ToList();
    if (gisFeatures.Count > 0)
    {
      featureClassElements = gisFeatures.Select(o => _iGisFeatureConverter.Convert(o)).ToList();
    }
    else // V2 compatibility with QGIS (still using GisFeature class)
    {
      List<GisFeature> oldGisFeatures = target.elements.Where(o => o is GisFeature).Cast<GisFeature>().ToList();
      featureClassElements = oldGisFeatures.Select(o => _gisFeatureConverter.Convert(o)).ToList();
    }

    return featureClassElements;
  }

  private void SetCrsDataOnReceive(VectorLayer target)
  {
    // create Spatial Reference (i.e. Coordinate Reference System - CRS)
    string wktString = string.Empty;
    if (target.crs is not null && target.crs.wkt is not null)
    {
      wktString = target.crs.wkt;
    }

    // ATM, GIS commit CRS is stored per layer, but should be moved to the Root level too, and created once per Receive
    ACG.SpatialReference spatialRef = ACG.SpatialReferenceBuilder.CreateSpatialReference(wktString);

    double trueNorthRadians = System.Convert.ToDouble((target.crs?.rotation == null) ? 0 : target.crs.rotation);
    double latOffset = System.Convert.ToDouble((target.crs?.offset_y == null) ? 0 : target.crs.offset_y);
    double lonOffset = System.Convert.ToDouble((target.crs?.offset_x == null) ? 0 : target.crs.offset_x);
    _contextStack.Current.Document.ActiveCRSoffsetRotation = new CRSoffsetRotation(
      spatialRef,
      latOffset,
      lonOffset,
      trueNorthRadians
    );
  }
}
