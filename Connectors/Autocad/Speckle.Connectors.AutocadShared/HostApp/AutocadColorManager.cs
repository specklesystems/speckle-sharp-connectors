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

  /// <summary>
  /// For receive operations
  /// </summary>
  public Dictionary<string, AutocadColor> ObjectColorsIdMap { get; } = new();

  /// <summary>
  /// For send operations
  /// </summary>
  private Dictionary<string, ColorProxy> ColorProxies { get; } = new();
  private readonly Dictionary<string, AutocadColor> _layerColorDict = new(); // keeps track of layer colors for object inheritance
  private readonly Dictionary<string, string> _objectsByLayerDict = new(); // keeps track of ids for all objects that inherited their color by layer

  /// <summary>
  /// Processes an object's color and adds the object id to a color proxy in <see cref="ColorProxies"/> if object color is set ByAci, ByColor, or ByBlock.
  /// Otherwise, stores the object id and color in a corresponding ByLayer dictionary for further processing block definitions after all objects are converted.
  /// From testing, a definition object will inherit its layer's color if by layer, otherwise it will inherit the instance color settings (which we are sending with the instance).
  /// Skips processing ByPen for now, because I don't understand what this means.
  /// </summary>
  /// <param name="objectId"></param>
  /// <param name="color"></param>
  private void ProcessObjectColor(string objectId, AutocadColor color, string? layerId = null)
  {
    switch (color.ColorMethod)
    {
      case ColorMethod.ByAci:
      case ColorMethod.ByColor:
      case ColorMethod.ByBlock:
        AddObjectIdToColorProxy(objectId, color);
        break;
      case ColorMethod.ByLayer:
        if (layerId != null)
        {
#if NET8_0
          _objectsByLayerDict.TryAdd(objectId, layerId);
#else
          if (!_objectsByLayerDict.ContainsKey(objectId))
          {
            _objectsByLayerDict.Add(objectId, layerId);
          }
#endif
        }
        break;
      case ColorMethod.ByPen: // POC: no idea what this means
        break;
    }
  }

  private void AddObjectIdToColorProxy(string objectId, AutocadColor color)
  {
    string colorId = color.GetSpeckleApplicationId();
    if (ColorProxies.TryGetValue(colorId, out ColorProxy? proxy))
    {
      proxy.objects.Add(objectId);
    }
    else
    {
      ColorProxy newColor = ConvertColorToColorProxy(color);
      newColor.objects.Add(objectId);
      ColorProxies[colorId] = newColor;
    }
  }

  private ColorProxy ConvertColorToColorProxy(AutocadColor color)
  {
    int argb = color.ColorValue.ToArgb();
    string name = color.ColorNameForDisplay;
    string id = color.GetSpeckleApplicationId();

    ColorProxy colorProxy = new(argb, id, name) { objects = new() };

    // add the color source as well for receiving in other apps
    colorProxy["source"] = color.IsByBlock
      ? "block"
      : color.IsByLayer
        ? "layer"
        : "object";

    // set additional properties if by aci or by block
    // ByBlock colors for some reason do not have their color value set to the correct color (white): instead it's a near-black
    // ByACI is an Autocad internal index for set rgb values, which effects name presentation, see: https://gohtx.com/acadcolors.php
    if (color.IsByAci)
    {
      colorProxy["autocadColorIndex"] = (int)color.ColorIndex;
    }
    else if (color.IsByBlock)
    {
      colorProxy.value = -1;
    }

    return colorProxy;
  }

  /// <summary>
  /// Processes colors for definition objects that had their colors inherited. This method is in place primarily to process complex color inheritance in blocks.
  /// </summary>
  /// <returns></returns>
  /// <remarks>
  /// We are **always setting the color** (treating it as ColorMethod.ByColor) for definition objects with color "ByLayer" because this overrides instance color, to guarantee they look correct in the viewer and when receiving.
  /// </remarks>
  public void ProcessDefinitionObjects(List<InstanceDefinitionProxy> definitions)
  {
    // process all definition objects, while removing process objects from the by block color dict as necessary
    foreach (InstanceDefinitionProxy definition in definitions)
    {
      foreach (string objectId in definition.objects)
      {
        if (_objectsByLayerDict.TryGetValue(objectId, out string? layerId))
        {
          if (_layerColorDict.TryGetValue(layerId, out AutocadColor? layerColor))
          {
            AddObjectIdToColorProxy(objectId, layerColor);
          }
        }
      }
    }
  }

  /// <summary>
  /// Iterates through a given set of autocad objects, layers, and definitions to collect atomic object colors.
  /// </summary>
  /// <param name="unpackedAutocadRootObjects">atomic root objects, including definition objects</param>
  /// <param name="layers">layers used by atomic objects</param>
  /// <param name="definitions">definitions used by instances in atomic objects</param>
  /// <returns></returns>
  /// <remarks>
  /// Due to complications in color inheritance for blocks, we are processing block definition object colors last.
  /// </remarks>
  public List<ColorProxy> UnpackColors(
    List<AutocadRootObject> unpackedAutocadRootObjects,
    List<LayerTableRecord> layers,
    List<InstanceDefinitionProxy> definitions
  )
  {
    // Stage 1: unpack colors from objects
    foreach (AutocadRootObject rootObj in unpackedAutocadRootObjects)
    {
      Entity entity = rootObj.Root;
      ProcessObjectColor(rootObj.ApplicationId, entity.Color, entity.LayerId.ToString());
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      ProcessObjectColor(layer.GetSpeckleApplicationId(), layer.Color);
      _layerColorDict.Add(layer.Id.ToString(), layer.Color);
    }

    // Stage 3: process definition objects that inherited their colors
    ProcessDefinitionObjects(definitions);

    return ColorProxies.Values.ToList();
  }

  public AutocadColor ConvertColorProxyToColor(ColorProxy colorProxy)
  {
    AutocadColor color = colorProxy["autocadColorIndex"] is long index
      ? AutocadColor.FromColorIndex(ColorMethod.ByAci, (short)index)
      : colorProxy["byBlock"] is bool byBlock && byBlock
        ? AutocadColor.FromColorIndex(ColorMethod.ByBlock, 0)
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

      // skip any colors with source = layer, since object color default source is by layer
      if (colorProxy["source"] is string source && source == "layer")
      {
        continue;
      }

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
