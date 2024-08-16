using ArcGIS.Core.Data;
using Speckle.InterfaceGenerator;
using Speckle.Objects;
using Speckle.Sdk.Models;
using FieldDescription = ArcGIS.Core.Data.DDL.FieldDescription;

namespace Speckle.Converters.ArcGIS3.Utils;

[GenerateAutoInterface]
public class FeatureClassUtils : IFeatureClassUtils
{
  private readonly IArcGISFieldUtils _fieldsUtils;

  public FeatureClassUtils(IArcGISFieldUtils fieldsUtils)
  {
    _fieldsUtils = fieldsUtils;
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
