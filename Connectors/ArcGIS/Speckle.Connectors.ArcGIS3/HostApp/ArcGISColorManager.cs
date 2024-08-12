using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISColorManager
{
  private Dictionary<string, ColorProxy> ColorProxies { get; set; } = new();

  /// <summary>
  /// Iterates through a given set of arcGIS map members (layers containing objects) and collects their colors.
  /// </summary>
  /// <param name="mapMembersWithDisplayPriority"></param>
  /// <returns>A list of color proxies, where the application Id is argb value + display priority</returns>
  /// <remarks>
  /// In ArcGIS, map members contain a formula, which individual features contained in map members will use to calculate their color.
  /// Since display priority is important for ArcGIS layers, we are creating different Color Proxies for eg the same argb color value but different display priority.
  /// </remarks>
  public List<ColorProxy> UnpackColors(List<(MapMember, int)> mapMembersWithDisplayPriority)
  {
    foreach ((MapMember mapMember, int priority) in mapMembersWithDisplayPriority)
    {
      switch (mapMember)
      {
        // FeatureLayer colors will be processed per feature object
        case FeatureLayer featureLayer:
          ProcessFeatureLayerColors(featureLayer, priority);
          break;

        // RasterLayer object colors are converted as mesh vertex colors, but we need to store displayPriority on the raster layer. Default color is used for all rasters.
        case RasterLayer rasterLayer:
          ProcessRasterLayerColors(rasterLayer, priority);
          break;
      }
    }

    return ColorProxies.Values.ToList();
  }

  private string GetColorApplicationId(int argb, double order) => $"{argb}_{order}";

  // Adds the element id to the color proxy based on colorId if it exists in ColorProxies,
  // otherwise creates a new Color Proxy with the element id in the objects property
  private void AddElementIdToColorProxy(string elementAppId, string colorId, int displayPriority)
  {
    if (ColorProxies.TryGetValue(colorId, out ColorProxy? colorProxy))
    {
      colorProxy.objects.Add(elementAppId);
    }
    else
    {
      ColorProxy newProxy =
        new()
        {
          applicationId = colorId,
          objects = new() { elementAppId },
          name = colorId
        };

      newProxy["displayPriority"] = displayPriority;
      ColorProxies.Add(colorId, newProxy);
    }
  }

  private void ProcessRasterLayerColors(RasterLayer rasterLayer, int displayPriority)
  {
    string elementAppId = $"{rasterLayer.URI}_0"; // POC: explain why count = 0 here
    string colorId = GetColorApplicationId(-1, displayPriority); // We are using a default color of -1 for all raster layers
    AddElementIdToColorProxy(elementAppId, colorId, displayPriority);
  }

  private void ProcessFeatureLayerColors(FeatureLayer layer, int displayPriority)
  {
    // first get a list of layer fields
    // field names are unique, but often their alias is used instead by renderer headings
    // so we are storing both names and alieas in this dictionary for fast lookup
    // POC: adding aliases are not optimal, because they do not need to be unique && they can be the same as the name of another field
    Dictionary<string, FieldDescription> layerFieldDictionary = new();
    foreach (FieldDescription field in layer.GetFieldDescriptions())
    {
      layerFieldDictionary.TryAdd(field.Name, field);
      layerFieldDictionary.TryAdd(field.Alias, field);
    }

    CIMRenderer layerRenderer = layer.GetRenderer();
    int count = 1;
    using (RowCursor rowCursor = layer.Search())
    {
      while (rowCursor.MoveNext())
      {
        string elementAppId = $"{layer.URI}_{count}";
        using (Row row = rowCursor.Current)
        {
          // get row color
          int argb = GetLayerColorByRendererAndRow(layerRenderer, row, layerFieldDictionary);
          string colorId = GetColorApplicationId(argb, displayPriority);
          AddElementIdToColorProxy(elementAppId, colorId, displayPriority);
        }

        count++;
      }
    }
  }

  // Attempts to retrieve the color from a CIMSymbol
  private bool TryGetSymbolColor(CIMSymbol symbol, out int symbolColor)
  {
    symbolColor = -1;
    if (symbol.GetColor() is CIMColor cimColor)
    {
      symbolColor = cimColor.CIMColorToInt();
      return true;
    }
    else
    {
      return false;
    }
  }

  private bool TryGetUniqueRendererColor(
    CIMUniqueValueRenderer uniqueRenderer,
    Row row,
    Dictionary<string, FieldDescription> fields,
    out int color
  )
  {
    if (!TryGetSymbolColor(uniqueRenderer.DefaultSymbol.Symbol, out color)) // get default color
    {
      return false;
    }

    // note: usually there is only 1 group
    foreach (CIMUniqueValueGroup group in uniqueRenderer.Groups)
    {
      string[] headings = group.Heading.Split(",");
      List<string> usedFields = new();
      foreach (string heading in headings)
      {
        if (fields.TryGetValue(heading, out FieldDescription? headingField))
        {
          usedFields.Add(headingField.Name);
        }
      }

      // loop through all values in groups to see if any have met conditions that result in a different color
      foreach (CIMUniqueValueClass groupClass in group.Classes)
      {
        bool groupConditionsMet = true;
        foreach (CIMUniqueValue value in groupClass.Values)
        {
          // all field values have to match the row values
          for (int i = 0; i < usedFields.Count; i++)
          {
            if (value.FieldValues[i].Replace("<Null>", "") != Convert.ToString(row[usedFields[i]]))
            {
              groupConditionsMet = false;
              break;
            }
          }
        }

        // set the group color to class symbol color if conditions are met
        if (groupConditionsMet)
        {
          if (!TryGetSymbolColor(groupClass.Symbol.Symbol, out color))
          {
            return false;
          }
        }
      }
    }

    return true;
  }

  private bool TryGetGraduatedRendererColor(
    CIMClassBreaksRenderer graduatedRenderer,
    Row row,
    Dictionary<string, FieldDescription> fields,
    out int color
  )
  {
    if (!TryGetSymbolColor(graduatedRenderer.DefaultSymbol.Symbol, out color)) // get default color
    {
      return false;
    }

    string? usedField = null;
    if (fields.TryGetValue(graduatedRenderer.Field, out FieldDescription? field))
    {
      usedField = field.Name;
    }

    List<CIMClassBreak> reversedBreaks = new(graduatedRenderer.Breaks);
    reversedBreaks.Reverse();
    foreach (var rBreak in reversedBreaks)
    {
      // keep looping until the last matching condition
      if (Convert.ToDouble(row[usedField]) <= rBreak.UpperBound)
      {
        if (!TryGetSymbolColor(rBreak.Symbol.Symbol, out color)) // get default color
        {
          return false;
        }
      }
    }

    return true;
  }

  // Tries to retrieve the feature layer color by renderer and row, or a default color of -1
  private int GetLayerColorByRendererAndRow(CIMRenderer renderer, Row row, Dictionary<string, FieldDescription> fields)
  {
    // default color to white. this will be used if the renderer is not supported.
    int color = -1;

    // get color depending on renderer type
    switch (renderer)
    {
      case CIMSimpleRenderer simpleRenderer:
        if (!TryGetSymbolColor(simpleRenderer.Symbol.Symbol, out color))
        {
          // TODO: report partial success on color?
        }
        break;

      // unique renderers have groups of conditions that may affect the color of a feature
      // resulting in a different color than the default renderer symbol color
      case CIMUniqueValueRenderer uniqueRenderer:
        if (!TryGetUniqueRendererColor(uniqueRenderer, row, fields, out color)) // get default color
        {
          // TODO: report partial success on color, could not retrieve group color?
        }
        break;

      case CIMClassBreaksRenderer graduatedRenderer:
        if (!TryGetGraduatedRendererColor(graduatedRenderer, row, fields, out color)) // get default color
        {
          // TODO: report partial success on color, could not retrieve group color?
        }
        break;

      default:
        // TODO: report color partial conversion error, unsupported renderer e.g. CIMProportionalRenderer
        break;
    }

    return color;
  }
}
