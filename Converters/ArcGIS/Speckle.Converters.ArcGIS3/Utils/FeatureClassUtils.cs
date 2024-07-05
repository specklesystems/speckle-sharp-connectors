using ArcGIS.Core.Data;
using Objects.GIS;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Core.Models;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.Utils;

public class FeatureClassUtils : IFeatureClassUtils
{
  private readonly IArcGISFieldUtils _fieldsUtils;

  public FeatureClassUtils(IArcGISFieldUtils fieldsUtils)
  {
    _fieldsUtils = fieldsUtils;
  }

  public void AddFeaturesToTable(Table newFeatureClass, List<GisFeature> gisFeatures, List<FieldDescription> fields)
  {
    foreach (GisFeature feat in gisFeatures)
    {
      using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
      {
        newFeatureClass
          .CreateRow(
            _fieldsUtils.AssignFieldValuesToRow(
              rowBuffer,
              fields,
              feat.attributes.GetMembers(DynamicBaseMemberType.Dynamic)
            )
          )
          .Dispose();
      }
    }
  }

  public void AddFeaturesToFeatureClass(
    FeatureClass newFeatureClass,
    List<GisFeature> gisFeatures,
    List<FieldDescription> fields,
    ITypedConverter<IReadOnlyList<Base>, ACG.Geometry> gisGeometryConverter
  )
  {
    foreach (GisFeature feat in gisFeatures)
    {
      using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
      {
        if (feat.geometry != null)
        {
          List<Base> geometryToConvert = feat.geometry;
          ACG.Geometry nativeShape = gisGeometryConverter.Convert(geometryToConvert);
          rowBuffer[newFeatureClass.GetDefinition().GetShapeField()] = nativeShape;
        }
        else
        {
          throw new SpeckleConversionException("No geomerty to write");
        }

        // get attributes
        newFeatureClass
          .CreateRow(
            _fieldsUtils.AssignFieldValuesToRow(
              rowBuffer,
              fields,
              feat.attributes.GetMembers(DynamicBaseMemberType.Dynamic)
            )
          )
          .Dispose();
      }
    }
  }

  public void AddNonGISFeaturesToFeatureClass(
    FeatureClass newFeatureClass,
    List<(Base baseObj, ACG.Geometry convertedGeom)> featuresTuples,
    List<(FieldDescription, Func<Base, object?>)> fieldsAndFunctions
  )
  {
    foreach ((Base baseObj, ACG.Geometry geom) in featuresTuples)
    {
      using (RowBuffer rowBuffer = newFeatureClass.CreateRowBuffer())
      {
        ACG.Geometry newGeom = geom;

        // exception for Points: turn into MultiPoint layer
        if (geom is ACG.MapPoint pointGeom)
        {
          newGeom = new ACG.MultipointBuilderEx(
            new List<ACG.MapPoint>() { pointGeom },
            ACG.AttributeFlags.HasZ
          ).ToGeometry();
        }

        rowBuffer[newFeatureClass.GetDefinition().GetShapeField()] = newGeom;

        // set and pass attributes
        Dictionary<string, object?> attributes = new();
        foreach ((FieldDescription field, Func<Base, object?> function) in fieldsAndFunctions)
        {
          string key = field.AliasName;
          attributes[key] = function(baseObj);
        }
        // newFeatureClass.CreateRow(rowBuffer).Dispose(); // without extra attributes
        newFeatureClass
          .CreateRow(
            _fieldsUtils.AssignFieldValuesToRow(rowBuffer, fieldsAndFunctions.Select(x => x.Item1).ToList(), attributes)
          )
          .Dispose();
      }
    }
  }
}
