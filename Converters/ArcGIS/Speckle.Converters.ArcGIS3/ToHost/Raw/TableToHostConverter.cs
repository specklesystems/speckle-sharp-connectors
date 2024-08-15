using ArcGIS.Core.Data;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Core.Data.Exceptions;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class TableLayerToHostConverter : ITypedConverter<VectorLayer, Table>
{
  private readonly IFeatureClassUtils _featureClassUtils;
  private readonly IArcGISFieldUtils _fieldsUtils;
  private readonly ITypedConverter<List<Base>, List<(ACG.Geometry?, Dictionary<string, object?>)>> _gisFeatureConverter;
  private readonly IConversionContextStack<ArcGISDocument, ACG.Unit> _contextStack;

  public TableLayerToHostConverter(
    IFeatureClassUtils featureClassUtils,
    ITypedConverter<List<Base>, List<(ACG.Geometry?, Dictionary<string, object?>)>> gisFeatureConverter,
    IConversionContextStack<ArcGISDocument, ACG.Unit> contextStack,
    IArcGISFieldUtils fieldsUtils
  )
  {
    _featureClassUtils = featureClassUtils;
    _gisFeatureConverter = gisFeatureConverter;
    _gisFeatureConverter = gisFeatureConverter;
    _contextStack = contextStack;
    _fieldsUtils = fieldsUtils;
  }

  public Table Convert(VectorLayer target)
  {
    FileGeodatabaseConnectionPath fileGeodatabaseConnectionPath =
      new(_contextStack.Current.Document.SpeckleDatabasePath);
    Geodatabase geodatabase = new(fileGeodatabaseConnectionPath);
    SchemaBuilder schemaBuilder = new(geodatabase);

    // create Fields
    List<FieldDescription> fields = _fieldsUtils.GetFieldsFromSpeckleLayer(target);

    // getting rid of forbidden symbols in the class name: adding a letter in the beginning
    // https://pro.arcgis.com/en/pro-app/3.1/tool-reference/tool-errors-and-warnings/001001-010000/tool-errors-and-warnings-00001-00025-000020.htm
    string featureClassName = "speckleID_" + target.id;

    // delete FeatureClass if already exists
    foreach (TableDefinition fClassDefinition in geodatabase.GetDefinitions<TableDefinition>())
    {
      // will cause GeodatabaseCatalogDatasetException if doesn't exist in the database
      if (fClassDefinition.GetName() == featureClassName)
      {
        TableDescription existingDescription = new(fClassDefinition);
        schemaBuilder.Delete(existingDescription);
        schemaBuilder.Build();
      }
    }

    // Create Table
    try
    {
      TableDescription featureClassDescription = new(featureClassName, fields);
      TableToken featureClassToken = schemaBuilder.Create(featureClassDescription);
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
      Table newFeatureClass = geodatabase.OpenDataset<Table>(featureClassName);

      // convert all elements in this table class
      List<(ACG.Geometry?, Dictionary<string, object?>)> featureClassElements = _gisFeatureConverter.Convert(
        target.elements
      );

      // process features into rows
      if (featureClassElements.Count == 0)
      {
        // POC: REPORT CONVERTED WITH ERROR HERE
        return newFeatureClass;
      }

      geodatabase.ApplyEdits(() =>
      {
        foreach ((ACG.Geometry?, Dictionary<string, object?>) featureClassElement in featureClassElements)
        {
          using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
          {
            RowBuffer assignedRowBuffer = _fieldsUtils.AssignFieldValuesToRow(
              rowBuffer,
              fields,
              featureClassElement.Item2
            ); // assign atts
            newFeatureClass.CreateRow(assignedRowBuffer).Dispose();
          }
        }
      });

      return newFeatureClass;
    }
    catch (GeodatabaseException)
    {
      // POC: review the exception
      throw;
    }
  }
}
