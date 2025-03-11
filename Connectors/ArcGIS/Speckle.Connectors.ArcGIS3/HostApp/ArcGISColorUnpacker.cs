using ArcGIS.Desktop.Mapping;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.ArcGIS.HostApp;

public class ArcGISColorUnpacker
{
  /// <summary>
  /// Cache of all color proxies for converted features. Key is the Color proxy argb value.
  /// </summary>
  public Dictionary<int, ColorProxy> ColorProxyCache { get; } = new();

  /// <summary>
  /// Stores the current renderer (determined by mapMember)
  /// </summary>
  private AC.CIM.CIMRenderer? StoredRenderer { get; set; }

  /// <summary>
  /// Stores the current renderer (determined by tin mapmember)
  /// </summary>
  private AC.CIM.CIMTinRenderer? StoredTinRenderer { get; set; }

  /// <summary>
  /// Stores the used renderer fields from the layer
  /// </summary>
  private List<string> StoredRendererFields { get; set; }

  /// <summary>
  /// Stores an already processed color for current mapMember, to dbe used by all mapMember objects. Only applies to simple type renderers
  /// </summary>
  private int? StoredColor { get; set; }

  /// <summary>
  /// Stores a feature layer renderer to be used by <see cref="ProcessFeatureLayerColor"/> in <see cref="StoredRenderer"/>, any fields used by the renderer from the layer, and resets the <see cref="StoredColor"/> and <see cref="StoredRendererFields"/>
  /// </summary>
  /// <param name="featureLayer"></param>
  /// <exception cref="AC.CalledOnWrongThreadException">Must be called on MCT.</exception>
  public void StoreRendererAndFields(ADM.FeatureLayer featureLayer)
  {
    // field names are unique, but often their alias is used instead by renderer headings
    // so we are storing both names and alias in this dictionary for fast lookup
    // POC: adding aliases are not optimal, because they do not need to be unique && they can be the same as the name of another field
    Dictionary<string, string> layerFieldDictionary = new();
    foreach (ADM.FieldDescription field in featureLayer.GetFieldDescriptions())
    {
      layerFieldDictionary.TryAdd(field.Name, field.Name);
      layerFieldDictionary.TryAdd(field.Alias, field.Name);
    }

    // clear stored values
    StoredRendererFields = new();
    StoredColor = null;
    StoredRenderer = null;

    AC.CIM.CIMRenderer layerRenderer = featureLayer.GetRenderer();
    List<string> fields = new();
    bool isSupported = false;
    switch (layerRenderer)
    {
      case AC.CIM.CIMSimpleRenderer:
        isSupported = true;
        break;
      case AC.CIM.CIMUniqueValueRenderer uniqueValueRenderer:
        isSupported = true;
        fields = uniqueValueRenderer.Fields.ToList();
        break;
      case AC.CIM.CIMClassBreaksRenderer classBreaksRenderer:
        isSupported = true;
        fields.Add(classBreaksRenderer.Field);
        break;
      default:
        // TODO: log error here that a renderer is unsupported
        break;
    }

    if (isSupported)
    {
      StoredRenderer = layerRenderer;
      foreach (string field in fields)
      {
        if (layerFieldDictionary.TryGetValue(field, out string? fieldName))
        {
          StoredRendererFields.Add(fieldName);
        }
      }
    }
  }

  /// <summary>
  /// Stores a las layer renderer to be used by <see cref="ProcessLasLayerColor"/> in <see cref="StoredTinRenderer"/>
  /// </summary>
  /// <param name="lasLayer"></param>
  /// <exception cref="AC.CalledOnWrongThreadException">Must be called on MCT.</exception>
  public void StoreRenderer(ADM.LasDatasetLayer lasLayer)
  {
    // clear stored values
    StoredTinRenderer = null;

    // POC: not sure why we are only using the first renderer here
    AC.CIM.CIMTinRenderer layerRenderer = lasLayer.GetRenderers()[0];
    bool isSupported = false;
    switch (layerRenderer)
    {
      case AC.CIM.CIMTinUniqueValueRenderer:
        isSupported = true;
        break;
      default:
        // TODO: log error here that a renderer is unsupported
        break;
    }

    if (isSupported)
    {
      StoredTinRenderer = layerRenderer;
    }
  }

  /// <summary>
  /// Processes a las layer's point color by the stored <see cref="StoredRenderer"/>, and stores the point's id and color proxy to the <see cref="ColorProxyCache"/>.
  /// POC: logic probably can be combined with ProcessFeatureLayerColor.
  /// </summary>
  /// <param name="point"></param>
  public void ProcessLasLayerColor(ACD.Analyst3D.LasPoint point, string pointApplicationId)
  {
    // get the color from the renderer and point
    AC.CIM.CIMColor? color;
    switch (StoredTinRenderer)
    {
      case AC.CIM.CIMTinUniqueValueRenderer uniqueValueRenderer:
        color = GetPointColorByUniqueValueRenderer(uniqueValueRenderer, point);
        break;

      default:
        return;
    }

    // get or create the color proxy for the point
    int argb = CIMColorToInt(color ?? point.RGBColor);
    AddObjectIdToColorProxyCache(pointApplicationId, argb);
  }

  // Retrieves the las point color from a unique value renderer
  // unique renderers have groups of conditions that may affect the color of a feature
  // resulting in a different color than the default renderer symbol color
  private AC.CIM.CIMColor? GetPointColorByUniqueValueRenderer(
    AC.CIM.CIMTinUniqueValueRenderer renderer,
    ACD.Analyst3D.LasPoint point
  )
  {
    foreach (AC.CIM.CIMUniqueValueGroup group in renderer.Groups)
    {
      foreach (AC.CIM.CIMUniqueValueClass groupClass in group.Classes)
      {
        foreach (AC.CIM.CIMUniqueValue value in groupClass.Values)
        {
          // all field values have to match the row values
          for (int i = 0; i < value.FieldValues.Length; i++)
          {
            string groupValue = value.FieldValues[i].Replace("<Null>", "");
            object? pointValue = point.ClassCode;

            if (ValuesAreEqual(groupValue, pointValue))
            {
              return groupClass.Symbol.Symbol.GetColor();
            }
          }
        }
      }
    }

    return null;
  }

  /// <summary>
  /// Processes a feature layer's row color by the stored <see cref="StoredRenderer"/>, and stores the row's id and color proxy to the <see cref="ColorProxyCache"/>.
  /// </summary>
  /// <param name="row"></param>
  /// <returns></returns>
  /// <exception cref="ACD.Exceptions.GeodatabaseException"></exception>
  public void ProcessFeatureLayerColor(ACD.Row row, string rowApplicationId)
  {
    // if stored color is not null, this means the renderer was a simple renderer that applies to the entire layer, and was already created.
    // just add the row application id to the color proxy.
    if (StoredColor is int existingColorProxyId)
    {
      AddObjectIdToColorProxyCache(rowApplicationId, existingColorProxyId);
      return;
    }

    // get the color from the renderer and row
    AC.CIM.CIMColor? color = null;
    switch (StoredRenderer)
    {
      // simple renderers do not rely on fields, so the color can be retrieved from the renderer directly
      case AC.CIM.CIMSimpleRenderer simpleRenderer:
        color = simpleRenderer.Symbol.Symbol.GetColor();
        break;

      case AC.CIM.CIMUniqueValueRenderer uniqueValueRenderer:
        color = GetRowColorByUniqueValueRenderer(uniqueValueRenderer, row);
        break;

      case AC.CIM.CIMClassBreaksRenderer classBreaksRenderer:
        color = GetRowColorByClassBreaksRenderer(classBreaksRenderer, row);
        break;
    }

    if (color is null)
    {
      // TODO: log error or throw exception that color could not be retrieved
      return;
    }

    // get or create the color proxy for the row
    int argb = CIMColorToInt(color);
    AddObjectIdToColorProxyCache(rowApplicationId, argb);

    // store color if from simple renderer
    if (StoredRenderer is AC.CIM.CIMSimpleRenderer)
    {
      StoredColor = argb;
    }
  }

  // Retrieves the row color from a class breaks renderer
  // unique renderers have groups of conditions that may affect the color of a feature
  // resulting in a different color than the default renderer symbol color
  private AC.CIM.CIMColor? GetRowColorByClassBreaksRenderer(AC.CIM.CIMClassBreaksRenderer renderer, ACD.Row row)
  {
    AC.CIM.CIMColor? color = null;

    // get the default symbol color
    if (renderer.DefaultSymbol?.Symbol.GetColor() is AC.CIM.CIMColor defaultColor)
    {
      color = defaultColor;
    }

    // get the first stored field, since this renderer should only have 1 field
    double storedFieldValue = Convert.ToDouble(row[StoredRendererFields.First()]);

    List<AC.CIM.CIMClassBreak> reversedBreaks = new(renderer.Breaks);
    reversedBreaks.Reverse();
    foreach (var rBreak in reversedBreaks)
    {
      // keep looping until the last matching condition
      if (storedFieldValue <= rBreak.UpperBound)
      {
        if (rBreak.Symbol.Symbol.GetColor() is AC.CIM.CIMColor breakColor)
        {
          color = breakColor;
        }
        else
        {
          // TODO: log error here, could not retrieve break color from symbol
        }
      }
    }

    return color;
  }

  // Retrieves the row color from a unique value renderer
  // unique renderers have groups of conditions that may affect the color of a feature
  // resulting in a different color than the default renderer symbol color
  private AC.CIM.CIMColor? GetRowColorByUniqueValueRenderer(AC.CIM.CIMUniqueValueRenderer renderer, ACD.Row row)
  {
    AC.CIM.CIMColor? color = null;

    // get the default symbol color
    if (renderer.DefaultSymbol?.Symbol.GetColor() is AC.CIM.CIMColor defaultColor)
    {
      color = defaultColor;
    }

    // note: usually there is only 1 group
    foreach (AC.CIM.CIMUniqueValueGroup group in renderer.Groups)
    {
      // loop through all values in groups to see if any have met conditions that result in a different color
      foreach (AC.CIM.CIMUniqueValueClass groupClass in group.Classes)
      {
        bool groupConditionsMet = true;
        foreach (AC.CIM.CIMUniqueValue value in groupClass.Values)
        {
          // all field values have to match the row values
          for (int i = 0; i < StoredRendererFields.Count; i++)
          {
            string groupValue = value.FieldValues[i];
            object? rowValue = row[StoredRendererFields[i]];

            if (!ValuesAreEqual(groupValue, rowValue))
            {
              groupConditionsMet = false;
              break;
            }
          }
        }

        // set the group color to class symbol color if conditions are met
        if (groupConditionsMet)
        {
          if (groupClass.Symbol.Symbol.GetColor() is AC.CIM.CIMColor groupColor)
          {
            color = groupColor;
          }
          else
          {
            // TODO: log error here, could not retrieve group color from symbol
          }
        }
      }
    }

    return color;
  }

  /// <summary>
  /// Compares the label string of a UniqueValueRenderer (groupValue), and an object value (row, las point), to determine if they are equal
  /// </summary>
  /// <param name="objectValue"></param>
  /// <param name="groupValue"></param>
  private bool ValuesAreEqual(string groupValue, object? objectValue)
  {
    switch (objectValue)
    {
      case int:
      case short:
      case long:
      case byte:
        string objectValueString = Convert.ToString(objectValue) ?? "";
        return groupValue.Equals(objectValueString);

      case string:
        return groupValue.Equals(objectValue);

      // POC: these are tricky to compare with the label strings accurately, so will trim both values to 5 decimal places.
      case double d:
        return double.TryParse(groupValue, out double groupDouble) && groupDouble - d < 0.000001;
      case float f:
        return float.TryParse(groupValue, out float groupFloat) && groupFloat - f < 0.000001;

      default:
        return false;
    }
  }

  private void AddObjectIdToColorProxyCache(string objectId, int argb)
  {
    if (ColorProxyCache.TryGetValue(argb, out ColorProxy? colorProxy))
    {
      colorProxy.objects.Add(objectId);
    }
    else
    {
      ColorProxy newColorProxy =
        new()
        {
          name = argb.ToString(),
          objects = new() { objectId },
          value = argb,
          applicationId = argb.ToString()
        };

      ColorProxyCache.Add(argb, newColorProxy);
    }
  }

  private int ArgbToInt(int a, int r, int g, int b)
  {
    return (a << 24) | (r << 16) | (g << 8) | b;
  }

  // Gets the argb int from a CIMColor
  // Defaults to assuming CIMColor.Values represent the red, green, and blue channels.
  private int CIMColorToInt(AC.CIM.CIMColor color)
  {
    switch (color)
    {
      case AC.CIM.CIMHSVColor hsv:
        (float hsvR, float hsvG, float hsvB) = RgbFromHsv(hsv.H, hsv.S, hsv.V);
        return ArgbToInt(
          (int)Math.Round(hsv.Alpha),
          (int)Math.Round(hsvR * 255),
          (int)Math.Round(hsvG * 255),
          (int)Math.Round(hsvB * 255)
        );

      case AC.CIM.CIMCMYKColor cmyk:
        float k = cmyk.K;
        int cmykR = Convert.ToInt32(255 * (1 - cmyk.C) * (1 - k));
        int cmykG = Convert.ToInt32(255 * (1 - cmyk.M) * (1 - k));
        int cmykB = Convert.ToInt32(255 * (1 - cmyk.Y) * (1 - k));
        return ArgbToInt((int)Math.Round(cmyk.Alpha), cmykR, cmykG, cmykB);

      default:
        return ArgbToInt(
          (int)Math.Round(color.Alpha),
          (int)Math.Round(color.Values[0]),
          (int)Math.Round(color.Values[1]),
          (int)Math.Round(color.Values[2])
        );
    }
  }

  private (float, float, float) RgbFromHsv(float hue, float saturation, float value)
  {
    // Translates HSV color to RGB color
    // H: 0.0 - 360.0, S: 0.0 - 100.0, V: 0.0 - 100.0
    // R, G, B: 0.0 - 1.0

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

    return (r, g, b);
  }
}
