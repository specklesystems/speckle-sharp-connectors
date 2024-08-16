using System.Drawing;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISColorManager
{
  private Dictionary<string, ColorProxy> ColorProxies { get; set; } = new();
  public Dictionary<string, Color> ObjectColorsIdMap { get; set; } = new();

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

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorsIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="colorProxies"></param>
  /// <param name="onOperationProgressed"></param>
  public void ParseColors(List<ColorProxy> colorProxies, Action<string, double?>? onOperationProgressed)
  {
    // injected as Singleton, so we need to clean existing proxies first
    ObjectColorsIdMap = new();
    var count = 0;
    foreach (ColorProxy colorProxy in colorProxies)
    {
      onOperationProgressed?.Invoke("Converting colors", (double)++count / colorProxies.Count);
      foreach (string objectId in colorProxy.objects)
      {
        Color convertedColor = Color.FromArgb(colorProxy.value);
        ObjectColorsIdMap.TryAdd(objectId, convertedColor);
      }
    }
  }

  /// <summary>
  /// Create a new CIMUniqueValueClass for UniqueRenderer per each object ID
  /// </summary>
  /// <param name="tc"></param>
  /// <param name="speckleGeometryType"></param>
  private CIMUniqueValueClass CreateColorCategory(TraversalContext tc, esriGeometryType speckleGeometryType)
  {
    Base baseObj = tc.Current;

    // declare default white color
    Color color = Color.FromArgb(255, 255, 255, 255);

    // get color moving upwards from the object
    foreach (var parent in tc.GetAscendants())
    {
      if (parent.applicationId is string appId && ObjectColorsIdMap.TryGetValue(appId, out Color objColor))
      {
        color = objColor;
        break;
      }
    }

    CIMSymbolReference symbol = CreateSymbol(speckleGeometryType, color);

    // First create a "CIMUniqueValueClass"
    List<CIMUniqueValue> listUniqueValues = new() { new CIMUniqueValue { FieldValues = new string[] { baseObj.id } } };

    CIMUniqueValueClass newUniqueValueClass =
      new()
      {
        Editable = true,
        Label = baseObj.id,
        Patch = PatchShape.Default,
        Symbol = symbol,
        Visible = true,
        Values = listUniqueValues.ToArray()
      };
    return newUniqueValueClass;
  }

  /// <summary>
  /// Create a Symbol from GeometryType and Color
  /// </summary>
  /// <param name="speckleGeometryType"></param>
  /// <param name="color"></param>
  private CIMSymbolReference CreateSymbol(esriGeometryType speckleGeometryType, Color color)
  {
    var symbol = SymbolFactory
      .Instance.ConstructPointSymbol(ColorFactory.Instance.CreateColor(color))
      .MakeSymbolReference();

    switch (speckleGeometryType)
    {
      case esriGeometryType.esriGeometryLine:
      case esriGeometryType.esriGeometryPolyline:
        symbol = SymbolFactory
          .Instance.ConstructLineSymbol(ColorFactory.Instance.CreateColor(color))
          .MakeSymbolReference();
        break;
      case esriGeometryType.esriGeometryPolygon:
      case esriGeometryType.esriGeometryMultiPatch:
        symbol = SymbolFactory
          .Instance.ConstructPolygonSymbol(ColorFactory.Instance.CreateColor(color))
          .MakeSymbolReference();
        break;
    }

    return symbol;
  }

  /// <summary>
  /// Add CIMUniqueValueClass to Layer Renderer (if exists); apply Renderer to Layer (again)
  /// </summary>
  /// <param name="tc"></param>
  /// <param name="trackerItem"></param>
  public async Task SetOrEditLayerRenderer(TraversalContext tc, ObjectConversionTracker trackerItem)
  {
    if (trackerItem.HostAppMapMember is not FeatureLayer fLayer)
    {
      // do nothing with non-feature layers
      return;
    }

    // declare default grey color, create default symbol for the given layer geometry type
    var color = Color.FromArgb(ColorFactory.Instance.GreyRGB.CIMColorToInt());
    CIMSymbolReference defaultSymbol = CreateSymbol(fLayer.ShapeType, color);

    // get existing renderer classes
    List<CIMUniqueValueClass> listUniqueValueClasses = new() { };
    var existingRenderer = QueuedTask.Run(() => fLayer.GetRenderer()).Result;
    // should be always UniqueRenderer, it's the only type we are creating atm
    if (existingRenderer is CIMUniqueValueRenderer uniqueRenderer)
    {
      if (uniqueRenderer.Groups[0].Classes != null)
      {
        listUniqueValueClasses.AddRange(uniqueRenderer.Groups[0].Classes.ToList());
      }
    }

    // Add new CIMUniqueValueClass (or multiple, if it's a Collection with elements, e.g. VectorLayer)
    List<TraversalContext> traversalContexts = new();
    if (tc.Current is Collection collection)
    {
      foreach (var element in collection.elements)
      {
        TraversalContext newTc = new(element, "elements", tc);
        traversalContexts.Add(newTc);
      }
    }
    else
    {
      traversalContexts.Add(tc);
    }

    foreach (var tContext in traversalContexts)
    {
      CIMUniqueValueClass newUniqueValueClass = CreateColorCategory(tContext, fLayer.ShapeType);
      if (!listUniqueValueClasses.Select(x => x.Label).Contains(newUniqueValueClass.Label))
      {
        listUniqueValueClasses.Add(newUniqueValueClass);
      }
    }

    // Create a list of CIMUniqueValueGroup
    CIMUniqueValueGroup uvg = new() { Classes = listUniqueValueClasses.ToArray(), };
    List<CIMUniqueValueGroup> listUniqueValueGroups = new() { uvg };
    // Create the CIMUniqueValueRenderer
    CIMUniqueValueRenderer uvr =
      new()
      {
        UseDefaultSymbol = true,
        DefaultLabel = "all other values",
        DefaultSymbol = defaultSymbol,
        Groups = listUniqueValueGroups.ToArray(),
        Fields = new string[] { "Speckle_ID" }
      };

    // Set the feature layer's renderer.
    await QueuedTask.Run(() => fLayer.SetRenderer(uvr)).ConfigureAwait(false);
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

  /// <summary>
  /// Record colors from every feature of the layer into ColorProxies
  /// </summary>
  /// <param name="layer"></param>
  /// <param name="displayPriority"></param>
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
    // Translates HSV color to RGB color
    // H: 0.0 - 360.0, S: 0.0 - 100.0, V: 0.0 - 100.0
    // R, G, B: 0.0 - 1.0

    float hue = hsvColor.H;
    float saturation = hsvColor.S;
    float value = hsvColor.V;

    float c = (value / 100) * (saturation / 100);
    float x = c * (1 - Math.Abs(((hue / 60) % 2) - 1));
    float m = (value / 100) - c;

    float r = 0;
    float g = 0;
    float b = 0;

    if (hue >= 0 && hue < 60)
    {
      r = c;
      g = x;
      b = 0;
    }
    else if (hue >= 60 && hue < 120)
    {
      r = x;
      g = c;
      b = 0;
    }
    else if (hue >= 120 && hue < 180)
    {
      r = 0;
      g = c;
      b = x;
    }
    else if (hue >= 180 && hue < 240)
    {
      r = 0;
      g = x;
      b = c;
    }
    else if (hue >= 240 && hue < 300)
    {
      r = x;
      g = 0;
      b = c;
    }
    else if (hue >= 300 && hue < 360)
    {
      r = c;
      g = 0;
      b = x;
    }

    r += m;
    g += m;
    b += m;

    // convert rgb 0.0-1.0 float to int
    int red = (int)Math.Round(r * 255);
    int green = (int)Math.Round(g * 255);
    int blue = (int)Math.Round(b * 255);

    return RbgToInt(255, red, green, blue);
  }

  /// <summary>
  /// Converts HSV color values to RGB
  /// </summary>
  /// <param name="h">0 - 360</param>
  /// <param name="s">0 - 100</param>
  /// <param name="v">0 - 100</param>
  /// <param name="r">0 - 255</param>
  /// <param name="g">0 - 255</param>
  /// <param name="b">0 - 255</param>
  private int HSVToRGB(int h, int s, int v, out int r, out int g, out int b)
  {
    var rgb = new int[3];

    var baseColor = (h + 60) % 360 / 120;
    var shift = (h + 60) % 360 - (120 * baseColor + 60);
    var secondaryColor = (baseColor + (shift >= 0 ? 1 : -1) + 3) % 3;

    //Setting Hue
    rgb[baseColor] = 255;
    rgb[secondaryColor] = (int)((Math.Abs(shift) / 60.0f) * 255.0f);

    //Setting Saturation
    for (var i = 0; i < 3; i++)
    {
      rgb[i] += (int)((255 - rgb[i]) * ((100 - s) / 100.0f));
    }

    //Setting Value
    for (var i = 0; i < 3; i++)
    {
      rgb[i] -= (int)(rgb[i] * (100 - v) / 100.0f);
    }

    r = rgb[0];
    g = rgb[1];
    b = rgb[2];

    return RbgToInt(255, r, g, b);
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

  /// <summary>
  /// Make comparable the Label string of a UniqueValueRenderer (groupValue), and a Feature Attribute value (rowValue)
  /// </summary>
  /// <param name="rowValue"></param>
  /// <param name="groupValue"></param>
  private (string, string) MakeValuesComparable(object? rowValue, string groupValue)
  {
    string newGroupValue = groupValue;
    string newRowValue = Convert.ToString(rowValue) ?? "";

    // int, doubles are tricky to compare with strings, trimming both to 5 digits
    if (rowValue is int or short or long)
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
