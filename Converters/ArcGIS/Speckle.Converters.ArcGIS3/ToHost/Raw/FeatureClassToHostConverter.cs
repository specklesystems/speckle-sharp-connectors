using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Exceptions;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class FeatureClassToHostConverter : ITypedConverter<VectorLayer, FeatureClass>
{
  private readonly ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> _gisGeometryConverter;
  private readonly ITypedConverter<IGisFeature, (ACG.Geometry, Dictionary<string, object?>)> _gisFeatureConverter;
  private readonly IFeatureClassUtils _featureClassUtils;
  private readonly IArcGISFieldUtils _fieldsUtils;
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public FeatureClassToHostConverter(
    ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> gisGeometryConverter,
    ITypedConverter<IGisFeature, (ACG.Geometry, Dictionary<string, object?>)> gisFeatureConverter,
    IFeatureClassUtils featureClassUtils,
    IArcGISFieldUtils fieldsUtils,
    IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack
  )
  {
    _gisGeometryConverter = gisGeometryConverter;
    _gisFeatureConverter = gisFeatureConverter;
    _featureClassUtils = featureClassUtils;
    _fieldsUtils = fieldsUtils;
    _contextStack = contextStack;
  }

  private List<GisFeature> RecoverOutdatedGisFeatures(VectorLayer target)
  {
    List<GisFeature> gisFeatures = new();
    foreach (Base baseElement in target.elements)
    {
      if (baseElement is GisFeature feature)
      {
        gisFeatures.Add(feature);
      }
    }
    return gisFeatures;
  }

  public FeatureClass Convert(VectorLayer target)
  {
    ACG.GeometryType geomType = GISLayerGeometryType.GetNativeLayerGeometryType(target);

    FileGeodatabaseConnectionPath fileGeodatabaseConnectionPath =
      new(_contextStack.Current.Document.SpeckleDatabasePath);
    Geodatabase geodatabase = new(fileGeodatabaseConnectionPath);
    SchemaBuilder schemaBuilder = new(geodatabase);

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

    // create Fields
    List<FieldDescription> fields = _fieldsUtils.GetFieldsFromSpeckleLayer(target);

    // getting rid of forbidden symbols in the class name: adding a letter in the beginning
    // https://pro.arcgis.com/en/pro-app/3.1/tool-reference/tool-errors-and-warnings/001001-010000/tool-errors-and-warnings-00001-00025-000020.htm
    string featureClassName = "speckleID_" + target.id;

    // delete FeatureClass if already exists
    foreach (FeatureClassDefinition fClassDefinition in geodatabase.GetDefinitions<FeatureClassDefinition>())
    {
      // will cause GeodatabaseCatalogDatasetException if doesn't exist in the database
      if (fClassDefinition.GetName() == featureClassName)
      {
        FeatureClassDescription existingDescription = new(fClassDefinition);
        schemaBuilder.Delete(existingDescription);
        schemaBuilder.Build();
      }
    }

    // Create FeatureClass
    try
    {
      // POC: make sure class has a valid crs
      ShapeDescription shpDescription = new(geomType, spatialRef) { HasZ = true };
      FeatureClassDescription featureClassDescription = new(featureClassName, fields, shpDescription);
      FeatureClassToken featureClassToken = schemaBuilder.Create(featureClassDescription);
    }
    catch (ArgumentException)
    {
      // POC: review the exception
      // if name has invalid characters/combinations
      throw;
    }
    bool buildStatus = schemaBuilder.Build();
    if (!buildStatus)
    {
      // POC: log somewhere the error in building the feature class
      IReadOnlyList<string> errors = schemaBuilder.ErrorMessages;
    }

    try
    {
      FeatureClass newFeatureClass = geodatabase.OpenDataset<FeatureClass>(featureClassName);

      // process IGisFeature
      List<IGisFeature> gisFeatures = target.elements.Where(o => o is IGisFeature).Cast<IGisFeature>().ToList();
      if (gisFeatures.Count > 0)
      {
        geodatabase.ApplyEdits(() =>
        {
          foreach (IGisFeature feat in gisFeatures)
          {
            using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
            {
              (ACG.Geometry nativeShape, Dictionary<string, object?> attributes) = _gisFeatureConverter.Convert(feat);
              rowBuffer[newFeatureClass.GetDefinition().GetShapeField()] = nativeShape;
              RowBuffer assignedRowBuffer = _fieldsUtils.AssignFieldValuesToRow(rowBuffer, fields, attributes); // assign atts
              newFeatureClass.CreateRow(assignedRowBuffer).Dispose();
            }
          }
        });
      }
      else // V2 compatibility with QGIS (still using GisFeature class)
      {
        List<GisFeature> oldGisFeatures = target.elements.Where(o => o is GisFeature).Cast<GisFeature>().ToList();
        geodatabase.ApplyEdits(() =>
        {
          _featureClassUtils.AddFeaturesToFeatureClass(newFeatureClass, oldGisFeatures, fields, _gisGeometryConverter);
        });
      }

      return newFeatureClass;
    }
    catch (GeodatabaseException)
    {
      // POC: review the exception
      throw;
    }
  }
}
