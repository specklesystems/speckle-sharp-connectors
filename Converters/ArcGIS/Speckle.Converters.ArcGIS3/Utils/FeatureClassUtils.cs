using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Exceptions;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.Utils;

[GenerateAutoInterface]
public class FeatureClassUtils : IFeatureClassUtils
{
  private readonly IArcGISFieldUtils _fieldsUtils;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public FeatureClassUtils(
    IArcGISFieldUtils fieldsUtils,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _fieldsUtils = fieldsUtils;
    _settingsStore = settingsStore;
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
      // "The table was not found." | System.InvalidCast
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
    FileGeodatabaseConnectionPath fileGeodatabaseConnectionPath = new(_settingsStore.Current.SpeckleDatabasePath);
    Geodatabase geodatabase = new(fileGeodatabaseConnectionPath);

    return geodatabase;
  }

  public async Task<Dictionary<string, List<(TraversalContext, ObjectConversionTracker)>>> GroupConversionTrackers(
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker,
    Action<string, double?> onOperationProgressed
  )
  {
    // 1. Sort features into groups by path and geom type
    double count = 0;
    Dictionary<string, List<(TraversalContext, ObjectConversionTracker)>> geometryGroups = new();
    foreach (var item in conversionTracker)
    {
      TraversalContext context = item.Key;
      ObjectConversionTracker trackerItem = item.Value;
      ACG.Geometry? geom = trackerItem.HostAppGeom;
      string? datasetId = trackerItem.DatasetId;

      // Add dictionnary item if doesn't exist yet
      // Key must be unique per parent and speckleType
      // Adding Offsets/rotation to Unique key, so the modified CAD geometry doesn't overwrite non-modified one
      // or, same commit received with different Offsets are saved to separate datasets
      // Also, keep char limit for dataset name under 128: https://pro.arcgis.com/en/pro-app/latest/help/data/geodatabases/manage-saphana/enterprise-geodatabase-limits.htm

      string speckleType = trackerItem.Base.speckle_type.Split(".")[^1];
      speckleType = speckleType.Length > 10 ? speckleType[..9] : speckleType;
      string? parentId = context.Parent?.Current.id;

      CRSoffsetRotation activeSR = _settingsStore.Current.ActiveCRSoffsetRotation;
      string xOffset = Convert.ToString(activeSR.LonOffset).Replace(".", "_");
      xOffset = xOffset.Length > 15 ? xOffset[..14] : xOffset;

      string yOffset = Convert.ToString(activeSR.LatOffset).Replace(".", "_");
      yOffset = yOffset.Length > 15 ? yOffset[..14] : yOffset;

      string trueNorth = Convert.ToString(activeSR.TrueNorthRadians).Replace(".", "_");
      trueNorth = trueNorth.Length > 10 ? trueNorth[..9] : trueNorth;

      // text: 36 symbols, speckleTYPE: 10, sr: 10, offsets: 40, id: 32 = 128
      string uniqueKey =
        $"speckle_{speckleType}_SR_{activeSR.SpatialReference.Name[..Math.Min(15, activeSR.SpatialReference.Name.Length - 1)]}_X_{xOffset}_Y_{yOffset}_North_{trueNorth}_speckleID_{parentId}";

      // for gis elements, use a parent layer ID
      if (item.Key.Parent?.Current is SGIS.VectorLayer vLayer)
      {
        uniqueKey = "speckleID_" + vLayer.id;
      }

      if (!geometryGroups.TryGetValue(uniqueKey, out _))
      {
        geometryGroups[uniqueKey] = new List<(TraversalContext, ObjectConversionTracker)>();
      }

      // record changes in conversion tracker
      trackerItem.AddDatasetId(uniqueKey);
      trackerItem.AddDatasetRow(geometryGroups[uniqueKey].Count);
      conversionTracker[item.Key] = trackerItem;

      geometryGroups[uniqueKey].Add((context, trackerItem));
      ClearExistingDataset(uniqueKey);

      onOperationProgressed.Invoke("Grouping features into layers", count++ / conversionTracker.Count);
      await Task.Yield();
    }

    return geometryGroups;
  }

  public async Task CreateDatasets(
    Dictionary<TraversalContext, ObjectConversionTracker> conversionTracker,
    Dictionary<string, List<(TraversalContext, ObjectConversionTracker)>> featureClassElements,
    Action<string, double?> onOperationProgressed
  )
  {
    double count = 0;
    Geodatabase geodatabase = GetDatabase();

    foreach (var datasetGroup in featureClassElements)
    {
      string featureClassName = datasetGroup.Key;
      List<(TraversalContext, ObjectConversionTracker)> listOfContextAndTrackers = datasetGroup.Value;

      // Get Fields and attributeFunction
      List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions = _fieldsUtils.GetFieldsAndAttributeFunctions(
        listOfContextAndTrackers
      );

      // Get geomType
      ACG.GeometryType geomType = DefineDatasetGeomType(listOfContextAndTrackers);

      try
      {
        // Create Table
        if (geomType == ACG.GeometryType.Unknown)
        {
          CreateTable(
            featureClassName,
            fieldsAndFunctions,
            geodatabase,
            listOfContextAndTrackers.Select(x => x.Item2).ToList()
          );

          onOperationProgressed.Invoke("Writing to Database", count++ / featureClassElements.Count);
          continue;
        }

        // Create new FeatureClass
        CreateFeatureClass(
          featureClassName,
          geomType,
          fieldsAndFunctions,
          geodatabase,
          listOfContextAndTrackers.Select(x => x.Item2).ToList()
        );
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

      onOperationProgressed.Invoke("Writing to Database", count++ / featureClassElements.Count);
      await Task.Yield();
    }
  }

  private void CreateFeatureClass(
    string featureClassName,
    ACG.GeometryType geomType,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    Geodatabase geodatabase,
    List<ObjectConversionTracker> conversionTrackers
  )
  {
    SchemaBuilder schemaBuilder = new(geodatabase);
    List<FieldDescription> fields = fieldsAndFunctions.Select(x => x.Item1).ToList();

    try
    {
      ShapeDescription shpDescription =
        new(geomType, _settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference) { HasZ = true };
      FeatureClassDescription featureClassDescription = new(featureClassName, fields, shpDescription);
      FeatureClassToken featureClassToken = schemaBuilder.Create(featureClassDescription);
    }
    catch (ArgumentException ex)
    {
      // if name has invalid characters/combinations
      // or 'The table contains multiple fields with the same name.:
      throw new ArgumentException($"{ex.Message}: {featureClassName}", ex);
    }
    if (!schemaBuilder.Build())
    {
      // POC: log somewhere the error in building the feature class
      IReadOnlyList<string> errors = schemaBuilder.ErrorMessages;
    }

    FeatureClass newFeatureClass = geodatabase.OpenDataset<FeatureClass>(featureClassName);
    geodatabase.ApplyEdits(() =>
    {
      WriteFeaturesToDataset(newFeatureClass, fieldsAndFunctions, conversionTrackers);
    });
  }

  private void CreateTable(
    string featureClassName,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    Geodatabase geodatabase,
    List<ObjectConversionTracker> conversionTrackers
  )
  {
    SchemaBuilder schemaBuilder = new(geodatabase);
    List<FieldDescription> fields = fieldsAndFunctions.Select(x => x.Item1).ToList();

    try
    {
      TableDescription featureClassDescription = new(featureClassName, fields);
      TableToken featureClassToken = schemaBuilder.Create(featureClassDescription);
    }
    catch (ArgumentException ex)
    {
      // if name has invalid characters/combinations
      // or 'The table contains multiple fields with the same name.:
      throw new ArgumentException($"{ex.Message}: {featureClassName}", ex);
    }
    if (!schemaBuilder.Build())
    {
      // POC: log somewhere the error in building the feature class
      IReadOnlyList<string> errors = schemaBuilder.ErrorMessages;
    }

    Table newFeatureClass = geodatabase.OpenDataset<Table>(featureClassName);
    geodatabase.ApplyEdits(() =>
    {
      WriteFeaturesToTable(newFeatureClass, fieldsAndFunctions, conversionTrackers);
    });
  }

  private ACG.GeometryType DefineDatasetGeomType(
    List<(TraversalContext, ObjectConversionTracker)> listOfContextAndTrackers
  )
  {
    ACG.GeometryType geomType;
    if (listOfContextAndTrackers.FirstOrDefault().Item1.Parent?.Current is SGIS.VectorLayer vLayer) // GIS
    {
      geomType = GISLayerGeometryType.GetNativeLayerGeometryType(vLayer);
    }
    else // non-GIS
    {
      var hostAppGeom = listOfContextAndTrackers[0].Item2.HostAppGeom;
      if (hostAppGeom is null) // type check, should not happen
      {
        throw new SpeckleConversionException("Conversion failed");
      }
      geomType = hostAppGeom.GeometryType;
    }

    return geomType;
  }

  public void WriteFeaturesToDataset(
    FeatureClass newFeatureClass,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    List<ObjectConversionTracker> listOfTrackers
  )
  {
    List<FieldDescription> fields = fieldsAndFunctions.Select(x => x.Item1).ToList();

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
        Dictionary<string, object?> attributes = _fieldsUtils.GetAttributesViaFunction(trackerItem, fieldsAndFunctions);

        // newFeatureClass.CreateRow(rowBuffer).Dispose(); // without extra attributes
        RowBuffer assignedRowBuffer = _fieldsUtils.AssignFieldValuesToRow(
          rowBuffer,
          fields,
          attributes // trackerItem.HostAppObjAttributes ?? new Dictionary<string, object?>()
        );
        newFeatureClass.CreateRow(assignedRowBuffer).Dispose();
      }
    }
  }

  public void WriteFeaturesToTable(
    Table newFeatureClass,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions,
    List<ObjectConversionTracker> listOfTrackers
  )
  {
    List<FieldDescription> fields = fieldsAndFunctions.Select(x => x.Item1).ToList();
    foreach (ObjectConversionTracker trackerItem in listOfTrackers)
    {
      using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
      {
        Dictionary<string, object?> attributes = _fieldsUtils.GetAttributesViaFunction(trackerItem, fieldsAndFunctions);

        // newFeatureClass.CreateRow(rowBuffer).Dispose(); // without extra attributes
        RowBuffer assignedRowBuffer = _fieldsUtils.AssignFieldValuesToRow(
          rowBuffer,
          fields,
          attributes // trackerItem.HostAppObjAttributes ?? new Dictionary<string, object?>()
        );
        newFeatureClass.CreateRow(assignedRowBuffer).Dispose();
      }
    }
  }
}
