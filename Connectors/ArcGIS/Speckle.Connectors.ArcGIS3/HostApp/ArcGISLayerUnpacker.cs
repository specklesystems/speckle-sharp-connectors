using ArcGIS.Core.Data;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

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

  public VectorLayer ConvertVectorLayer(FeatureLayer featureLayer)
  {
    VectorLayer convertedVectorLayer = new();

    // get feature class fields
    var allLayerAttributes = new Base();
    var dispayTable = featureLayer as IDisplayTable;

    // POC: this should be refactored into a stored method of supported/unsupported field types, since this logic is duplicated in GisFeature converter
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
    convertedVectorLayer.attributes = allLayerAttributes;

    // get a simple geometry type
    string speckleGeometryType = GISLayerGeometryType.LayerGeometryTypeToSpeckle(featureLayer.ShapeType);
    convertedVectorLayer.geomType = speckleGeometryType;
    return convertedVectorLayer;
  }

  public Collection AddLayerWithProps(
    string applicationId,
    MapMember mapMember,
    string globalUnits,
    CRSoffsetRotation activeCRS
  )
  {
    Collection converted = new();

    if (mapMember is FeatureLayer featureLayer)
    {
      converted = ConvertVectorLayer(featureLayer);
    }

    // get common attributes for any type of layer
    // get units & Active CRS (for writing geometry coords)
    converted["units"] = globalUnits;
    var spatialRef = activeCRS.SpatialReference;
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
