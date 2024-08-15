using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(FeatureLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class VectorLayerToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<FeatureLayer, VectorLayer>
{
  private readonly ITypedConverter<(Row, string), IGisFeature> _gisFeatureConverter;
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;

  public VectorLayerToSpeckleConverter(
    ITypedConverter<(Row, string), IGisFeature> gisFeatureConverter,
    IConversionContextStack<ArcGISDocument, Unit> contextStack
  )
  {
    _gisFeatureConverter = gisFeatureConverter;
    _contextStack = contextStack;
  }

  public Base Convert(object target)
  {
    return Convert((FeatureLayer)target);
  }

  public VectorLayer Convert(FeatureLayer target)
  {
    VectorLayer speckleLayer = new();

    // get feature class fields
    var allLayerAttributes = new Base();
    var dispayTable = target as IDisplayTable;
    HashSet<string> visibleFieldDescriptions = new();
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

        visibleFieldDescriptions.Add(field.Name);
        allLayerAttributes[name] = GISAttributeFieldType.FieldTypeToSpeckle(field.Type);
      }
    }
    speckleLayer.attributes = allLayerAttributes;

    // get a simple geometry type
    string spekleGeometryType = GISLayerGeometryType.LayerGeometryTypeToSpeckle(target.ShapeType);
    speckleLayer.geomType = spekleGeometryType;

    // search the rows
    // RowCursor is IDisposable but is not being correctly picked up by IDE warnings.
    // This means we need to be carefully adding using statements based on the API documentation coming from each method/class

    int count = 1;
    using (RowCursor rowCursor = target.Search())
    {
      while (rowCursor.MoveNext())
      {
        // Same IDisposable issue appears to happen on Row class too. Docs say it should always be disposed of manually by the caller.
        using (Row row = rowCursor.Current)
        {
          string appId = $"{target.URI}_{count}";
          IGisFeature element = _gisFeatureConverter.Convert((row, appId));

          // create new element attributes from the existing attributes, based on the vector layer visible fields
          Base elementAttributes = new();
          foreach (string elementAtt in element.attributes.GetDynamicPropertyKeys())
          {
            if (visibleFieldDescriptions.Contains(elementAtt))
            {
              elementAttributes[elementAtt] = element.attributes[elementAtt];
            }
          }

          element.attributes = elementAttributes;
          speckleLayer.elements.Add((Base)element);
        }
        count++;
      }
    }

    return speckleLayer;
  }
}
