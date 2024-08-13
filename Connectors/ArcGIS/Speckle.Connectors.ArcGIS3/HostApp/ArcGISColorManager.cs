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
    // injected as Singleton, so we need to clean existing proxies first
    ColorProxies = new();

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
  private void AddElementIdToColorProxy(string elementAppId, int colorValue, string colorId, int displayPriority)
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
          value = colorValue,
          applicationId = colorId,
          objects = new() { elementAppId },
          name = colorId
        };

      newProxy["displayOrder"] = displayPriority; // 0 - top layer (top display priority), 1,2,3.. decreasing priority
      ColorProxies.Add(colorId, newProxy);
    }
  }

  private void ProcessRasterLayerColors(RasterLayer rasterLayer, int displayPriority)
  {
    string elementAppId = $"{rasterLayer.URI}_0"; // POC: explain why count = 0 here
    int argb = -1;
    string colorId = GetColorApplicationId(argb, displayPriority); // We are using a default color of -1 for all raster layers
    AddElementIdToColorProxy(elementAppId, argb, colorId, displayPriority);
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
          AddElementIdToColorProxy(elementAppId, argb, colorId, displayPriority);
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
      switch (cimColor)
      {
        case CIMRGBColor rgbColor:
          symbolColor = rgbColor.CIMColorToInt();
          return true;
        case CIMHSVColor hsvColor:
          symbolColor = RgbFromHsv(hsvColor);
          return true;
        case CIMCMYKColor cmykColor:
          symbolColor = RgbFromCmyk(cmykColor);
          return true;
        default:
          return false;
      }
    }
    else
    {
      return false;
    }
  }

  private int RbgToInt(int a, int r, int g, int b)
  {
    return (a << 24) | (r << 16) | (g << 8) | b;
  }

  private int RgbFromCmyk(CIMCMYKColor cmykColor)
  {
    float c = cmykColor.C;
    float m = cmykColor.M;
    float y = cmykColor.Y;
    float k = cmykColor.K;

    int r = Convert.ToInt32(255 * (1 - c) * (1 - k));
    int g = Convert.ToInt32(255 * (1 - m) * (1 - k));
    int b = Convert.ToInt32(255 * (1 - y) * (1 - k));
    return RbgToInt(255, r, g, b);
  }

  private int RgbFromHsv(CIMHSVColor hsvColor)
  {
    float hue = hsvColor.H;
    float saturation = hsvColor.S;
    float value = hsvColor.V;

    int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
    double f = hue / 60 - Math.Floor(hue / 60);

    saturation /= 255;
    int v = Convert.ToInt32(value);
    int p = Convert.ToInt32(value * (1 - saturation));
    int q = Convert.ToInt32(value * (1 - f * saturation));
    int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

    switch (hi)
    {
      case 0:
        return RbgToInt(255, v, t, p);
      case 1:
        return RbgToInt(255, q, v, p);
      case 2:
        return RbgToInt(255, p, v, t);
      case 3:
        return RbgToInt(255, p, q, v);
      case 4:
        return RbgToInt(255, t, p, v);
      default:
        return RbgToInt(255, v, p, q);
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
            string groupValue = value.FieldValues[i].Replace("<Null>", "");
            object? rowValue = row[usedFields[i]];

            (string newRowValue, string newGroupValue) = MakeValuesComparable(rowValue, groupValue);
            if (newGroupValue != newRowValue)
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

  private (string, string) MakeValuesComparable(object? rowValue, string groupValue)
  {
    string newGroupValue = groupValue;
    string newRowValue = Convert.ToString(rowValue) ?? "";

    // int, doubles are tricky to compare with strings, trimming both to 5 digits
    if (rowValue is int || rowValue is Int16 || rowValue is Int64)
    {
      newRowValue = newRowValue.Split(".")[0];
      newGroupValue = newGroupValue.Split(".")[0];
    }
    else if (rowValue is double || rowValue is float)
    {
      newRowValue = string.Concat(
        newRowValue.Split(".")[0],
        ".",
        newRowValue.Split(".")[^1].AsSpan(0, Math.Min(5, newRowValue.Split(".")[^1].Length))
      );
      newGroupValue = string.Concat(
        newGroupValue.Split(".")[0],
        ".",
        newGroupValue.Split(".")[^1].AsSpan(0, Math.Min(5, newGroupValue.Split(".")[^1].Length))
      );
    }

    return (newRowValue, newGroupValue);
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
          // POC: report CONVERTED WITH WARNING when implemented
        }
        break;

      // unique renderers have groups of conditions that may affect the color of a feature
      // resulting in a different color than the default renderer symbol color
      case CIMUniqueValueRenderer uniqueRenderer:
        if (!TryGetUniqueRendererColor(uniqueRenderer, row, fields, out color)) // get default color
        {
          // POC: report CONVERTED WITH WARNING when implemented
        }
        break;

      case CIMClassBreaksRenderer graduatedRenderer:
        if (!TryGetGraduatedRendererColor(graduatedRenderer, row, fields, out color)) // get default color
        {
          // POC: report CONVERTED WITH WARNING when implemented
        }
        break;

      default:
        // POC: report CONVERTED WITH WARNING when implemented, unsupported renderer e.g. CIMProportionalRenderer
        break;
    }

    return color;
  }
}
