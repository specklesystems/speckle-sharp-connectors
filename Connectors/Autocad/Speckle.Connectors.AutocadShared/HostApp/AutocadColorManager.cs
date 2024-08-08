using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Sdk.Models.Proxies;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadColorManager
{
  // POC: Will be addressed to move it into AutocadContext!
  private Document Doc => Application.DocumentManager.MdiActiveDocument;

  public Dictionary<string, AutocadColor> ObjectColorsIdMap { get; } = new();

  private ColorProxy ConvertColorToColorProxy(AutocadColor color, string id)
  {
    int argb = color.ColorValue.ToArgb();
    string name = color.ColorNameForDisplay;

    ColorProxy colorProxy = new(argb, id, name) { objects = new() };

    // INFO: this index is an Autocad internal index for set rgb values
    // https://gohtx.com/acadcolors.php
    if (color.IsByAci)
    {
      colorProxy["autocadColorIndex"] = (int)color.ColorIndex;
    }

    return colorProxy;
  }

  /// <summary>
  /// Iterates through a given set of autocad objects and collects their colors. Note: expects objects to be "atomic", and extracted out of their instances already.
  /// </summary>
  /// <param name="unpackedAutocadRootObjects"></param>
  /// <param name="layers"></param>
  /// <returns></returns>
  public List<ColorProxy> UnpackColors(
    List<AutocadRootObject> unpackedAutocadRootObjects,
    List<LayerTableRecord> layers
  )
  {
    Dictionary<string, ColorProxy> colorProxies = new();

    // Stage 1: unpack colors from objects
    foreach (AutocadRootObject rootObj in unpackedAutocadRootObjects)
    {
      Entity entity = rootObj.Root;

      // skip any objects that inherit their colors
      if (!entity.Color.IsByAci && !entity.Color.IsByColor)
      {
        continue;
      }

      // assumes color names are unique
      string colorId = entity.Color.ColorNameForDisplay;

      if (colorProxies.TryGetValue(colorId, out ColorProxy value))
      {
        value.objects.Add(rootObj.ApplicationId);
      }
      else
      {
        ColorProxy newColor = ConvertColorToColorProxy(entity.Color, colorId);
        newColor.objects.Add(rootObj.ApplicationId);
        colorProxies[colorId] = newColor;
      }
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      // assumes color names are unique
      string colorId = layer.Color.ColorNameForDisplay;
      string layerId = layer.Handle.ToString();

      if (colorProxies.TryGetValue(colorId, out ColorProxy value))
      {
        value.objects.Add(layerId);
      }
      else
      {
        ColorProxy newColor = ConvertColorToColorProxy(layer.Color, colorId);
        newColor.objects.Add(layerId);
        colorProxies[colorId] = newColor;
      }
    }

    return colorProxies.Values.ToList();
  }

  public AutocadColor ConvertColorProxyToColor(ColorProxy colorProxy)
  {
    AutocadColor color = colorProxy["autocadColorIndex"] is long index
      ? AutocadColor.FromColorIndex(ColorMethod.ByAci, (short)index)
      : AutocadColor.FromColor(System.Drawing.Color.FromArgb(colorProxy.value));

    return color;
  }

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="colorProxies"></param>
  /// <param name="onOperationProgressed"></param>
  public void ParseColors(List<ColorProxy> colorProxies, Action<string, double?>? onOperationProgressed)
  {
    var count = 0;
    foreach (ColorProxy colorProxy in colorProxies)
    {
      onOperationProgressed?.Invoke("Converting colors", (double)++count / colorProxies.Count);
      foreach (string objectId in colorProxy.objects)
      {
        AutocadColor convertedColor = ConvertColorProxyToColor(colorProxy);

        if (!ObjectColorsIdMap.ContainsKey(objectId))
        {
          ObjectColorsIdMap.Add(objectId, convertedColor);
        }
      }
    }
  }
}
