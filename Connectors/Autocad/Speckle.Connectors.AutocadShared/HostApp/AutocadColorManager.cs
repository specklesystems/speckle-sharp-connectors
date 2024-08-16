using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Sdk.Models.Instances;
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

  private ColorProxy ConvertColorToColorProxy(AutocadColor color)
  {
    int argb = color.ColorValue.ToArgb();
    string name = color.ColorNameForDisplay;
    string id = color.GetSpeckleApplicationId();

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
  /// <remarks>
  /// Due to complications in color inheritance for blocks, we are simplifying the behavior to **always setting the color** (treating it as ColorMethod.ByColor) for definition objects to guarantee they look correct in the viewer and when receiving.
  /// </remarks>
  public List<ColorProxy> UnpackColors(
    List<AutocadRootObject> unpackedAutocadRootObjects,
    List<LayerTableRecord> layers,
    List<InstanceDefinitionProxy> definitions
  )
  {
    Dictionary<string, ColorProxy> colorProxies = new();

    // Stage 1: unpack colors from objects
    Dictionary<string, AutocadColor> objectInheritedColorDict = new(); // keeps track of color ids for all atomic objects that inherited their color
    foreach (AutocadRootObject rootObj in unpackedAutocadRootObjects)
    {
      Entity entity = rootObj.Root;
      string objectId = rootObj.ApplicationId;

      // skip any objects that inherit their colors for now
      if (!entity.Color.IsByAci && !entity.Color.IsByColor)
      {
        if (!objectInheritedColorDict.ContainsKey(objectId))
        {
          objectInheritedColorDict.Add(objectId, entity.Color);
        }
        continue;
      }

      AddObjectIdToColorProxy(objectId, colorProxies, entity.Color);
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      // assumes color names are unique
      string layerId = layer.GetSpeckleApplicationId();
      AddObjectIdToColorProxy(layerId, colorProxies, layer.Color);
    }

    // Stage 3: retrieve definition object colors for any objects that inherited their color
    foreach (InstanceDefinitionProxy definition in definitions)
    {
      foreach (string objectId in definition.objects)
      {
        if (objectInheritedColorDict.TryGetValue(objectId, out AutocadColor? color))
        {
          AddObjectIdToColorProxy(objectId, colorProxies, color);
        }
      }
    }

    return colorProxies.Values.ToList();
  }

  private void AddObjectIdToColorProxy(string objectId, Dictionary<string, ColorProxy> proxies, AutocadColor color)
  {
    string colorId = color.GetSpeckleApplicationId();
    if (proxies.TryGetValue(colorId, out ColorProxy? proxy))
    {
      proxy.objects.Add(objectId);
    }
    else
    {
      ColorProxy newColor = ConvertColorToColorProxy(color);
      newColor.objects.Add(objectId);
      proxies[colorId] = newColor;
    }
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
#if NET8_0
        ObjectColorsIdMap.TryAdd(objectId, convertedColor);
#else
        if (!ObjectColorsIdMap.ContainsKey(objectId))
        {
          ObjectColorsIdMap.Add(objectId, convertedColor);
        }
#endif
      }
    }
  }
}
