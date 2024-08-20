using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Exceptions;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Speckle.Objects;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.Utils;

[GenerateAutoInterface]
public class FeatureClassUtils : IFeatureClassUtils
{
  private readonly IArcGISFieldUtils _fieldsUtils;
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public FeatureClassUtils(
    IArcGISFieldUtils fieldsUtils,
    IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack
  )
  {
    _fieldsUtils = fieldsUtils;
    _contextStack = contextStack;
  }

  public void ClearExistingDataset(string featureClassName)
  {
    Geodatabase geodatabase = GetDatabase();
    SchemaBuilder schemaBuilder = new(geodatabase);

    // getting rid of forbidden symbols in the class name: adding a letter in the beginning
    // https://pro.arcgis.com/en/pro-app/3.1/tool-reference/tool-errors-and-warnings/001001-010000/tool-errors-and-warnings-00001-00025-000020.htm


    // delete FeatureClass if already exists
    try
    {
      FeatureClassDefinition fClassDefinition = geodatabase.GetDefinition<FeatureClassDefinition>(featureClassName);
      FeatureClassDescription existingDescription = new(fClassDefinition);
      schemaBuilder.Delete(existingDescription);
      schemaBuilder.Build();
      MapView.Active.Redraw(true);
    }
    catch (Exception ex) when (!ex.IsFatal()) //(GeodatabaseTableException)
    {
      // "The table was not found."
      // delete Table if already exists
      try
      {
        TableDefinition fClassDefinition = geodatabase.GetDefinition<TableDefinition>(featureClassName);
        TableDescription existingDescription = new(fClassDefinition);
        schemaBuilder.Delete(existingDescription);
        schemaBuilder.Build();
        MapView.Active.Redraw(true);
      }
      catch (Exception ex2) when (!ex2.IsFatal()) //(GeodatabaseTableException)
      {
        // "The table was not found.", do nothing
      }
    }
  }

  private Geodatabase GetDatabase()
  {
    // get database
    FileGeodatabaseConnectionPath fileGeodatabaseConnectionPath =
      new(_contextStack.Current.Document.SpeckleDatabasePath);
    Geodatabase geodatabase = new(fileGeodatabaseConnectionPath);

    return geodatabase;
  }

  public Dictionary<string, (SGIS.VectorLayer?, List<ObjectConversionTracker>)> GroupGisConversionTrackers(
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker
  )
  {
    Dictionary<string, (SGIS.VectorLayer?, List<ObjectConversionTracker>)> featureClassElements = new();

    foreach (var trackerItem in conversionTracker)
    {
      TraversalContext tc = trackerItem.Key;
      ObjectConversionTracker tracker = trackerItem.Value;
      if (tc.Parent?.Current is not SGIS.VectorLayer vLayer || tracker.DatasetId is not string featureClassName)
      {
        continue; // not GIS elements
      }

      // add new Feature class, or add geometry to already added class
      bool added = featureClassElements.TryAdd(
        featureClassName,
        (vLayer, new List<ObjectConversionTracker>() { tracker })
      );
      if (!added)
      {
        featureClassElements[featureClassName].Item2.Add(tracker);
        ClearExistingDataset(featureClassName);
      }
    }

    return featureClassElements;
  }

  public Dictionary<string, (SGIS.VectorLayer?, List<ObjectConversionTracker>)> GroupNonGisConversionTrackers(
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker
  )
  {
    // 1. Sort features into groups by path and geom type
    Dictionary<string, (SGIS.VectorLayer?, List<ObjectConversionTracker>)> geometryGroups = new();
    foreach (var item in conversionTracker)
    {
      TraversalContext context = item.Key;
      ObjectConversionTracker trackerItem = item.Value;
      ACG.Geometry? geom = trackerItem.HostAppGeom;
      string? datasetId = trackerItem.DatasetId;

      if (geom != null && datasetId != null) // GIS elements
      {
        continue;
      }
      else if (geom != null && datasetId == null) // only non-native geomerties, not written into a dataset yet
      {
        // add dictionnary item if doesn't exist yet
        // Key must be unique per parent and speckleType
        // Adding Offsets/rotation to Unique key, so the modified CAD geometry doesn't overwrite non-modified one
        // or, same commit received with different Offsets are saved to separate datasets

        // Also, keep char limit for dataset name under 128: https://pro.arcgis.com/en/pro-app/latest/help/data/geodatabases/manage-saphana/enterprise-geodatabase-limits.htm

        string speckleType = trackerItem.Base.speckle_type.Split(".")[^1];
        //speckleType = speckleType.Substring(0, Math.Min(10, speckleType.Length - 1));
        speckleType = speckleType.Length > 10 ? speckleType[..9] : speckleType;
        string? parentId = context.Parent?.Current.id;

        CRSoffsetRotation activeSR = _contextStack.Current.Document.ActiveCRSoffsetRotation;
        string xOffset = Convert.ToString(activeSR.LonOffset).Replace(".", "_");
        xOffset = xOffset.Length > 15 ? xOffset[..14] : xOffset;

        string yOffset = Convert.ToString(activeSR.LatOffset).Replace(".", "_");
        yOffset = yOffset.Length > 15 ? yOffset[..14] : yOffset;

        string trueNorth = Convert.ToString(activeSR.TrueNorthRadians).Replace(".", "_");
        trueNorth = trueNorth.Length > 10 ? trueNorth[..9] : trueNorth;

        // text: 36 symbols, speckleTYPE: 10, sr: 10, offsets: 40, id: 32 = 128
        string uniqueKey =
          $"speckle_{speckleType}_SR_{activeSR.SpatialReference.Name[..Math.Min(15, activeSR.SpatialReference.Name.Length - 1)]}_X_{xOffset}_Y_{yOffset}_North_{trueNorth}_speckleID_{parentId}";

        if (!geometryGroups.TryGetValue(uniqueKey, out _))
        {
          geometryGroups[uniqueKey] = (null, new List<ObjectConversionTracker>());
        }

        // record changes in conversion tracker
        trackerItem.AddDatasetId(uniqueKey);
        trackerItem.AddDatasetRow(geometryGroups[uniqueKey].Item2.Count);
        conversionTracker[item.Key] = trackerItem;

        geometryGroups[uniqueKey].Item2.Add(trackerItem);
        ClearExistingDataset(uniqueKey);
      }
      else
      {
        throw new ArgumentException($"Unexpected geometry and datasetId values: {geom}, {datasetId}");
      }
    }

    return geometryGroups;
  }

  public void CreateDatasets(
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker,
    Dictionary<string, (SGIS.VectorLayer?, List<ObjectConversionTracker>)> featureClassElements,
    Action<string, double?>? onOperationProgressed
  )
  {
    int count = 0;
    Geodatabase geodatabase = GetDatabase();
    SchemaBuilder schemaBuilder = new(geodatabase);

    foreach (var item in featureClassElements)
    {
      string featureClassName = item.Key;
      SGIS.VectorLayer? vLayer = item.Value.Item1;
      List<ObjectConversionTracker> listOfTrackers = item.Value.Item2;

      // create Fields
      ACG.GeometryType geomType;
      List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions = new();
      List<FieldDescription> fields = new();

      // Get Fields and geom type separately for GIS and non-GIS
      if (vLayer is not null) // GIS
      {
        fields = _fieldsUtils.GetFieldsFromSpeckleLayer(vLayer);
        fieldsAndFunctions = fields
          .Select(x =>
            (
              x,
              (Func<Base, object?>)(x.Name == "Speckle_ID" ? y => y?.id : y => (y as IGisFeature)?.attributes[x.Name])
            )
          )
          .ToList();
        geomType = GISLayerGeometryType.GetNativeLayerGeometryType(vLayer);
      }
      else // non-GIS
      {
        fieldsAndFunctions = _fieldsUtils.CreateFieldsFromListOfBase(listOfTrackers.Select(x => x.Base).ToList());
        fields = fieldsAndFunctions.Select(x => x.Item1).ToList();
        var hostAppGeom = listOfTrackers[0].HostAppGeom;
        if (hostAppGeom is not ACG.Geometry geometry) // type check, should not happen
        {
          throw new SpeckleConversionException("Conversion failed");
        }
        geomType = geometry.GeometryType;
      }

      // Create new FeatureClass
      try
      {
        ShapeDescription shpDescription =
          new(geomType, _contextStack.Current.Document.ActiveCRSoffsetRotation.SpatialReference) { HasZ = true };
        FeatureClassDescription featureClassDescription = new(featureClassName, fields, shpDescription);
        FeatureClassToken featureClassToken = schemaBuilder.Create(featureClassDescription);
      }
      catch (ArgumentException ex)
      {
        // if name has invalid characters/combinations
        // or 'The table contains multiple fields with the same name.:
        throw new ArgumentException($"{ex.Message}: {featureClassName}", ex);
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
        geodatabase.ApplyEdits(() =>
        {
          WriteFeaturesToDataset(newFeatureClass, fieldsAndFunctions, listOfTrackers);
        });
      }
      catch (GeodatabaseException ex)
      {
        // do nothing if writing of some geometry groups fails
        // only record in conversionTracker:
        foreach (var conversionItem in conversionTracker)
        {
          if (conversionItem.Value.DatasetId == featureClassName)
          {
            var trackerItem = conversionItem.Value;
            trackerItem.AddException(ex);
            conversionTracker[conversionItem.Key] = trackerItem;
          }
        }
      }
      count += 1;
      onOperationProgressed?.Invoke("Writing to Database", (double)count / featureClassElements.Count);
    }
  }

  public void WriteFeaturesToDataset(
    FeatureClass newFeatureClass,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    List<ObjectConversionTracker> listOfTrackers
  )
  {
    foreach (ObjectConversionTracker trackerItem in listOfTrackers)
    {
      using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
      {
        if (trackerItem.HostAppGeom is not ACG.Geometry shape)
        {
          throw new SpeckleConversionException("Feature Class element had no converted geometry");
        }

        // exception for Points: turn into MultiPoint layer
        if (shape is ACG.MapPoint pointGeom)
        {
          shape = new ACG.MultipointBuilderEx(
            new List<ACG.MapPoint>() { pointGeom },
            ACG.AttributeFlags.HasZ
          ).ToGeometry();
        }

        rowBuffer[newFeatureClass.GetDefinition().GetShapeField()] = shape;

        // set and pass attributes
        Dictionary<string, object?> attributes = new();
        foreach ((FieldDescription field, Func<Base, object?> function) in fieldsAndFunctions)
        {
          string key = field.AliasName;
          attributes[key] = function(trackerItem.Base);
        }

        // newFeatureClass.CreateRow(rowBuffer).Dispose(); // without extra attributes
        RowBuffer assignedRowBuffer = _fieldsUtils.AssignFieldValuesToRow(
          rowBuffer,
          fieldsAndFunctions.Select(x => x.Item1).ToList(),
          attributes // trackerItem.HostAppObjAttributes ?? new Dictionary<string, object?>()
        );
        newFeatureClass.CreateRow(assignedRowBuffer).Dispose();
      }
    }
  }

  public void AddFeaturesToTable(Table newFeatureClass, List<IGisFeature> gisFeatures, List<FieldDescription> fields)
  {
    foreach (IGisFeature feat in gisFeatures)
    {
      using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
      {
        RowBuffer assignedRowBuffer = _fieldsUtils.AssignFieldValuesToRow(
          rowBuffer,
          fields,
          feat.attributes.GetMembers(DynamicBaseMemberType.Dynamic)
        );
        newFeatureClass.CreateRow(assignedRowBuffer).Dispose();
      }
    }
  }
}
