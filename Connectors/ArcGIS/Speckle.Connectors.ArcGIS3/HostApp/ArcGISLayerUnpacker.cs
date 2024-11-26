using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using RasterLayer = ArcGIS.Desktop.Mapping.RasterLayer;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISLayerUnpacker
{
  public void AddConvertedToRoot(
    string applicationId,
    Base converted,
    Collection rootObjectCollection,
    List<(ILayerContainer, Collection)> nestedGroups
  )
  {
    // add converted layer to Root or to sub-Collection

    if (nestedGroups.Count == 0 || nestedGroups.Count == 1 && nestedGroups[0].Item2.applicationId == applicationId)
    {
      // add to host if no groups, or current root group
      rootObjectCollection.elements.Add(converted);
    }
    else
    {
      // if we are adding a layer inside the group
      var parentCollection = nestedGroups.FirstOrDefault(x =>
        x.Item1.Layers.Select(y => y.URI).Contains(applicationId)
      );
      parentCollection.Item2.elements.Add(converted);
    }
  }

  public Base InsertNestedGroup(
    ILayerContainer layerContainer,
    string applicationId,
    List<(ILayerContainer, Collection)> nestedGroups
  )
  {
    // group layer will always come before it's contained layers
    // keep active group last in the list
    Base converted = new Collection() { name = ((MapMember)layerContainer).Name, applicationId = applicationId };
    nestedGroups.Insert(0, (layerContainer, (Collection)converted));
    return converted;
  }

  public void ResetNestedGroups(string applicationId, List<(ILayerContainer, Collection)> nestedGroups)
  {
    int groupCount = nestedGroups.Count; // bake here, because count will change in the loop

    // if the layer is not a part of the group, reset groups
    for (int i = 0; i < groupCount; i++)
    {
      if (nestedGroups.Count > 0 && !nestedGroups[0].Item1.Layers.Select(x => x.URI).Contains(applicationId))
      {
        nestedGroups.RemoveAt(0);
      }
      else
      {
        // break at the first group, which contains current layer
        break;
      }
    }
  }

  private Objects.GIS.RasterLayer ConvertRasterLayer(
    SpatialReference spatialRefGlobal,
    SpatialReference? spatialRefRaster
  )
  {
    Objects.GIS.RasterLayer convertedRasterLayer = new();
    // get active map CRS if layer CRS is empty
    if (spatialRefRaster?.Unit is null)
    {
      spatialRefRaster = spatialRefGlobal;
    }
    convertedRasterLayer.rasterCrs = new CRS
    {
      wkt = spatialRefRaster.Wkt,
      name = spatialRefRaster.Name,
      units_native = spatialRefRaster.Unit.ToString(),
    };
    return convertedRasterLayer;
  }

  private VectorLayer ConvertVectorLayer(MapMember mapMember)
  {
    VectorLayer convertedVectorLayer = new();

    // get feature class fields
    var allLayerAttributes = new Base();
    var dispayTable = mapMember as IDisplayTable;
    if (dispayTable is not null)
    {
      foreach (FieldDescription field in dispayTable.GetFieldDescriptions())
      {
        if (field.IsVisible)
        {
          string name = field.Name;
          if (
            field.Type == FieldType.Geometry
            || field.Type == FieldType.Raster
            || field.Type == FieldType.XML
            || field.Type == FieldType.Blob
          )
          {
            continue;
          }

          allLayerAttributes[name] = GISAttributeFieldType.FieldTypeToSpeckle(field.Type);
        }
      }
    }
    convertedVectorLayer.attributes = allLayerAttributes;

    // get a simple geometry type
    if (mapMember is FeatureLayer arcGisFeatureLayer)
    {
      convertedVectorLayer.geomType = GISLayerGeometryType.LayerGeometryTypeToSpeckle(arcGisFeatureLayer.ShapeType);
    }
    else if (mapMember is StandaloneTable)
    {
      convertedVectorLayer.geomType = GISLayerGeometryType.NONE;
    }

    return convertedVectorLayer;
  }

  private VectorLayer ConvertPointCloudLayer(LasDatasetLayer pointcloudLayer)
  {
    VectorLayer speckleLayer =
      new() { nativeGeomType = pointcloudLayer.MapLayerType.ToString(), geomType = GISLayerGeometryType.POINTCLOUD };

    return speckleLayer;
  }

  public async Task<Collection> AddLayerWithProps(
    string applicationId,
    MapMember mapMember,
    string globalUnits,
    CRSoffsetRotation activeCRS
  )
  {
    Collection converted = new();
    var spatialRef = activeCRS.SpatialReference;

    if (mapMember is FeatureLayer featureLayer)
    {
      converted = ConvertVectorLayer(featureLayer);
    }
    else if (mapMember is StandaloneTable tableLayer)
    {
      converted = ConvertVectorLayer(tableLayer);
    }
    else if (mapMember is RasterLayer arcGisRasterLayer)
    {
      // layer native crs (for writing properties e.g. resolution, origin etc.)
      SpatialReference? spatialRefRaster = await QueuedTask
        .Run(() => arcGisRasterLayer.GetSpatialReference())
        .ConfigureAwait(false);

      converted = ConvertRasterLayer(spatialRef, spatialRefRaster);
    }
    else if (mapMember is LasDatasetLayer pointcloudLayer)
    {
      converted = ConvertPointCloudLayer(pointcloudLayer);
    }

    // get common attributes for any type of layer
    // get units & Active CRS (for writing geometry coords)
    converted["units"] = globalUnits;
    converted["crs"] = new CRS
    {
      wkt = spatialRef.Wkt,
      name = spatialRef.Name,
      offset_y = Convert.ToSingle(activeCRS.LatOffset),
      offset_x = Convert.ToSingle(activeCRS.LonOffset),
      rotation = Convert.ToSingle(activeCRS.TrueNorthRadians),
      units_native = globalUnits // not 'units', because might need to potentially support 'degrees'
    };
    // other common properties for layers and groups
    converted["name"] = mapMember.Name;
    converted.applicationId = applicationId;

    return converted;
  }
}
