using System.Drawing;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.GIS;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(FeatureLayer), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class VectorLayerToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<FeatureLayer, VectorLayer>
{
  private readonly ITypedConverter<Row, GisFeature> _gisFeatureConverter;
  private readonly IConversionContextStack<ArcGISDocument, Unit> _contextStack;

  public VectorLayerToSpeckleConverter(
    ITypedConverter<Row, GisFeature> gisFeatureConverter,
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
    IReadOnlyList<FieldDescription> allFieldDescriptions = dispayTable.GetFieldDescriptions();
    List<FieldDescription> addedFieldDescriptions = new();
    foreach (FieldDescription field in allFieldDescriptions)
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
        addedFieldDescriptions.Add(field);
        allLayerAttributes[name] = GISAttributeFieldType.FieldTypeToSpeckle(field.Type);
      }
    }
    speckleLayer.attributes = allLayerAttributes;

    // get a simple geometry type
    string speckleGeometryType = GISLayerGeometryType.LayerGeometryTypeToSpeckle(target.ShapeType);
    speckleLayer.geomType = speckleGeometryType;

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
          GisFeature element = _gisFeatureConverter.Convert(row);
          element.applicationId = $"{target.URI}_{count}";

          // get and record feature color
          RecordFeatureColor(target, row, element);

          // replace element "attributes", to remove those non-visible on Layer level
          Base elementAttributes = new();
          foreach (FieldDescription field in addedFieldDescriptions)
          {
            if (field.IsVisible)
            {
              elementAttributes[field.Name] = element.attributes[field.Name];
            }
          }
          element.attributes = elementAttributes;
          speckleLayer.elements.Add(element);
        }

        count += 1;
      }
    }

    return speckleLayer;
  }

  private void RecordFeatureColor(FeatureLayer target, Row row, GisFeature element)
  {
    if (element.applicationId == null)
    {
      throw new SpeckleConversionException($"applicationId is not assigned to Feature in layer '{target.Name}'");
    }

    // get color from renderer, write to contextStack, assign to the feature
    int color = GetFeatureColor(target, target.GetFieldDescriptions(), row);

    bool materialExists = false;
    double priority = _contextStack.Current.Document.LayersInOperationIndices[target];

    foreach (var materialProxy in _contextStack.Current.Document.RenderMaterialProxies)
    {
      if (
        materialProxy.value.diffuse == color
        && materialProxy.value["displayPriority"] is double existingPriority
        && existingPriority == priority
      )
      {
        materialProxy.objects.Add(element.applicationId);
        materialExists = true;
        break;
      }
    }

    if (materialExists is false)
    {
      var material = new RenderMaterial() { diffuse = color, applicationId = $"{color}_{priority}" };
      material["displayPriority"] = priority;

      var newMaterialProxy = new RenderMaterialProxy(material, new List<string>() { element.applicationId });
      _contextStack.Current.Document.RenderMaterialProxies.Add(newMaterialProxy);
    }
  }

  private int GetFeatureColor(FeatureLayer fLayer, List<FieldDescription> fields, Row row)
  {
    int color = Color.FromArgb(255, 255, 255, 255).ToArgb();
    var renderer = fLayer.GetRenderer(); // e.g. CIMSimpleRenderer

    // get color depending on renderer type
    if (renderer is CIMSimpleRenderer simpleRenderer)
    {
      var simpleSymbolColor = simpleRenderer.Symbol.Symbol.GetColor();
      return simpleSymbolColor != null ? simpleSymbolColor.CIMColorToInt() : color;
    }

    if (renderer is CIMUniqueValueRenderer uniqueRenderer)
    {
      CIMColor? groupColor = uniqueRenderer.DefaultSymbol.Symbol.GetColor();
      // normally it would be 1 group
      foreach (var group in uniqueRenderer.Groups)
      {
        var headings = group.Heading.Split(",");
        // headings will use the Field Alias, not field Name.
        // get field names assuming Alias is used, if not found - then by Name
        var usedFields = headings.Select(x =>
          fields.FirstOrDefault(y => y.Alias == x) ?? fields.FirstOrDefault(y => y.Name == x)
        );
        var usedFieldNames = usedFields.Select(x => x?.Name).ToList();

        // keep looping until the last matching condition
        foreach (var groupClass in group.Classes)
        {
          foreach (var value in groupClass.Values)
          {
            // all field values have to match the row values
            for (int i = 0; i < usedFieldNames.Count; i++)
            {
              if (value.FieldValues[i].Replace("<Null>", "") != System.Convert.ToString(row[usedFieldNames[i]]))
              {
                break;
              }
              // if the loop covered all matching properties
              if (i == usedFieldNames.Count - 1)
              {
                groupColor = groupClass.Symbol.Symbol.GetColor();
              }
            }
          }
        }
      }
      return groupColor != null ? groupColor.CIMColorToInt() : color;
    }

    if (renderer is CIMClassBreaksRenderer graduatedRenderer)
    {
      CIMColor? breakColor = graduatedRenderer.DefaultSymbol.Symbol.GetColor();

      // get field name assuming Name is used, if not - Alias
      var usedField =
        fields.FirstOrDefault(y => y.Name == graduatedRenderer.Field)
        ?? fields.FirstOrDefault(y => y.Alias == graduatedRenderer.Field);
      var usedFieldName = usedField?.Name;

      var reversedBreaks = new List<CIMClassBreak>(graduatedRenderer.Breaks);
      reversedBreaks.Reverse();
      foreach (var rBreak in reversedBreaks)
      {
        // keep looping until the last matching condition
        if (System.Convert.ToDouble(row[usedFieldName]) <= rBreak.UpperBound)
        {
          breakColor = rBreak.Symbol.Symbol.GetColor();
        }
      }
      return breakColor != null ? breakColor.CIMColorToInt() : color;
    }

    // TODO: partial success case - show warning that the renderer {renderer.GetType().Name} is not applied. e.g. CIMProportionalRenderer
    return color;
  }
}
