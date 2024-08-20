using ArcGIS.Core.Data;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(VectorLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class VectorLayerToHostConverter : IToHostTopLevelConverter, ITypedConverter<VectorLayer, object>
{
  private readonly ITypedConverter<
    VectorLayer,
    List<(Base, ACG.Geometry, Dictionary<string, object?>)>
  > _featureClassConverter;
  private readonly ITypedConverter<VectorLayer, Table> _tableConverter;

  public VectorLayerToHostConverter(
    ITypedConverter<VectorLayer, List<(Base, ACG.Geometry, Dictionary<string, object?>)>> featureClassConverter,
    ITypedConverter<VectorLayer, Table> tableConverter
  )
  {
    _featureClassConverter = featureClassConverter;
    _tableConverter = tableConverter;
  }

  public object Convert(Base target) => Convert((VectorLayer)target);

  public object Convert(VectorLayer target)
  {
    // pointcloud layers need to be checked separately, because there is no ArcGIS Geometry type
    // for Pointcloud. In ArcGIS it's a completely different layer class, so "GetNativeLayerGeometryType"
    // will return "Invalid" type
    if (target.geomType == GISLayerGeometryType.POINTCLOUD)
    {
      // do nothing;
    }

    // check if Speckle VectorLayer should become a FeatureClass, StandaloneTable or PointcloudLayer
    ACG.GeometryType geomType = GISLayerGeometryType.GetNativeLayerGeometryType(target);
    if (geomType != ACG.GeometryType.Unknown) // feature class
    {
      return _featureClassConverter.Convert(target);
    }
    else // table
    {
      return _tableConverter.Convert(target);
    }
  }
}
