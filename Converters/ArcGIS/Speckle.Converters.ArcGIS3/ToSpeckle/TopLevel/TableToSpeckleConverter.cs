using ArcGIS.Core.Data;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(StandaloneTable), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class StandaloneTableToSpeckleConverter
  : IToSpeckleTopLevelConverter,
    ITypedConverter<StandaloneTable, VectorLayer>
{
  private readonly ITypedConverter<Row, GisFeature> _gisFeatureConverter;

  public StandaloneTableToSpeckleConverter(ITypedConverter<Row, GisFeature> gisFeatureConverter)
  {
    _gisFeatureConverter = gisFeatureConverter;
  }

  public Base Convert(object target)
  {
    return Convert((StandaloneTable)target);
  }

  public VectorLayer Convert(StandaloneTable target)
  {
    VectorLayer speckleLayer = new() { };

    // get feature class fields
    var attributes = new Base();
    var displayTable = target as IDisplayTable;
    HashSet<string> visibleFields = new();
    foreach (FieldDescription field in displayTable.GetFieldDescriptions())
    {
      if (field.IsVisible)
      {
        visibleFields.Add(field.Name);
        string name = field.Name;
        attributes[name] = (int)field.Type;
      }
    }

    speckleLayer.attributes = attributes;
    string spekleGeometryType = "None";

    using (RowCursor rowCursor = displayTable.Search())
    {
      while (rowCursor.MoveNext())
      {
        // Same IDisposable issue appears to happen on Row class too. Docs say it should always be disposed of manually by the caller.
        using (Row row = rowCursor.Current)
        {
          GisFeature element = _gisFeatureConverter.Convert(row);

          // create new element attributes from the existing attributes, based on the vector layer visible fields
          // POC: this should be refactored to store the feature layer properties in the context stack, so this logic can be done in the gisFeatureConverter
          Base elementAttributes = new();
          foreach (string elementAtt in element.attributes.GetDynamicPropertyKeys())
          {
            if (visibleFields.Contains(elementAtt))
            {
              elementAttributes[elementAtt] = element.attributes[elementAtt];
            }
          }

          element.attributes = elementAttributes;

          speckleLayer.elements.Add(element);
        }
      }
    }

    speckleLayer.geomType = spekleGeometryType;
    return speckleLayer;
  }
}
