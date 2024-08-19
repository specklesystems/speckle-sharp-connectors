using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Rhino.Extensions;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing colors on objects and layers. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoColorManager
{
  /// <summary>
  /// For receive operations
  /// </summary>
  public Dictionary<string, (Color, ObjectColorSource)> ObjectColorsIdMap { get; } = new();

  /// <summary>
  /// For send operations
  /// </summary>
  private Dictionary<string, ColorProxy> ColorProxies { get; } = new();
  private readonly Dictionary<int, Color> _layerColorDict = new(); // keeps track of layer colors for object inheritance
  private readonly Dictionary<string, int> _objectsByLayerDict = new(); // keeps track of ids for all objects that inherited their color by layer

  /// <summary>
  /// Processes an object's color and adds the object id to a color proxy in <see cref="ColorProxies"/> if object color is set ByColor, ByMaterial, or ByParent.
  /// Otherwise, stores the object id and color in a corresponding ByLayer dictionary for further processing block definitions after all objects are converted.
  /// From testing, a definition object will inherit its layer's color if by layer, otherwise it will inherit the instance color settings (which we are sending with the instance).
  /// </summary>
  /// <param name="objId"></param>
  /// <param name="color"></param>
  private void ProcessObjectColor(
    string objId,
    Color color,
    ObjectColorSource source,
    int? layerIndex = null,
    int? materialIndex = null
  )
  {
    switch (source)
    {
      case ObjectColorSource.ColorFromObject:
      case ObjectColorSource.ColorFromParent:
        AddObjectIdToColorProxy(objId, color, source);
        break;
      case ObjectColorSource.ColorFromMaterial:
        if (materialIndex is int materialIndexInt && RhinoDoc.ActiveDoc.Materials.Count > materialIndexInt)
        {
          AddObjectIdToColorProxy(objId, RhinoDoc.ActiveDoc.Materials[materialIndexInt].DiffuseColor, source);
        }
        break;
      case ObjectColorSource.ColorFromLayer:
        if (layerIndex is int layerIndexInt)
        {
#if NET8_0
          _objectsByLayerDict.TryAdd(objectId, layerIndexInt);
#else
          if (!_objectsByLayerDict.ContainsKey(objId))
          {
            _objectsByLayerDict.Add(objId, layerIndexInt);
          }
#endif
        }
        break;
    }
  }

  private void AddObjectIdToColorProxy(string objectId, Color color, ObjectColorSource source)
  {
    string colorId = color.GetSpeckleApplicationId(source);
    if (ColorProxies.TryGetValue(colorId, out ColorProxy? proxy))
    {
      proxy.objects.Add(objectId);
    }
    else
    {
      ColorProxy newColor = ConvertColorToColorProxy(color, source);
      newColor.objects.Add(objectId);
      ColorProxies[colorId] = newColor;
    }
  }

  private ColorProxy ConvertColorToColorProxy(Color color, ObjectColorSource source)
  {
    int argb = color.ToArgb();
    string id = color.GetSpeckleApplicationId(source);
    string? name = color.IsNamedColor ? color.Name : null;

    ColorProxy colorProxy = new(argb, id, name) { objects = new() };

    // add the color source as well for receiving in other apps
    colorProxy["source"] =
      source is ObjectColorSource.ColorFromParent
        ? "block"
        : source is ObjectColorSource.ColorFromLayer
          ? "layer"
          : source is ObjectColorSource.ColorFromMaterial
            ? "material"
            : "object";

    return colorProxy;
  }

  /// <summary>
  /// Processes colors for definition objects that had their colors inherited. This method is in place primarily to process complex color inheritance in blocks.
  /// </summary>
  /// <returns></returns>
  /// <remarks>
  /// We are **always setting the color** (treating it as ColorSource.ByObject) for definition objects with color "ByLayer" because this overrides instance color, to guarantee they look correct in the viewer and when receiving.
  /// </remarks>
  public void ProcessDefinitionObjects(List<InstanceDefinitionProxy> definitions)
  {
    // process all definition objects, while removing process objects from the by block color dict as necessary
    foreach (InstanceDefinitionProxy definition in definitions)
    {
      foreach (string objectId in definition.objects)
      {
        if (_objectsByLayerDict.TryGetValue(objectId, out int layerIndex))
        {
          if (_layerColorDict.TryGetValue(layerIndex, out Color layerColor))
          {
            AddObjectIdToColorProxy(objectId, layerColor, ObjectColorSource.ColorFromLayer);
          }
        }
      }
    }
  }

  /// <summary>
  /// Iterates through a given set of autocad objects, layers, and definitions to collect atomic object colors.
  /// </summary>
  /// <param name="atomicObjects">atomic root objects, including instance objects</param>
  /// <param name="layers">layers used by atomic objects</param>
  /// <param name="definitions">definitions used by instances in atomic objects</param>
  /// <returns></returns>
  /// <remarks>
  /// Due to complications in color inheritance for blocks, we are processing block definition object colors last.
  /// </remarks>
  public List<ColorProxy> UnpackColors(
    List<RhinoObject> atomicObjects,
    List<Layer> layers,
    List<InstanceDefinitionProxy> definitions
  )
  {
    // Stage 1: unpack colors from objects
    foreach (RhinoObject rootObj in atomicObjects)
    {
      ProcessObjectColor(
        rootObj.GetSpeckleApplicationId(),
        rootObj.Attributes.ObjectColor,
        rootObj.Attributes.ColorSource,
        rootObj.Attributes.LayerIndex,
        rootObj.Attributes.MaterialIndex
      );
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (Layer layer in layers)
    {
      ProcessObjectColor(layer.Id.ToString(), layer.Color, ObjectColorSource.ColorFromObject);
      _layerColorDict.Add(layer.Index, layer.Color);
    }

    // Stage 3: process definition objects that inherited their colors
    ProcessDefinitionObjects(definitions);

    return ColorProxies.Values.ToList();
  }

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorsIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="colorProxies"></param>
  public void ParseColors(List<ColorProxy> colorProxies)
  {
    foreach (ColorProxy colorProxy in colorProxies)
    {
      ObjectColorSource source = ObjectColorSource.ColorFromObject;
      if (colorProxy["source"] is string proxySource)
      {
        switch (proxySource)
        {
          case "layer":
            continue; // skip any colors with source = layer, since object color default source is by layer
          case "block":
            source = ObjectColorSource.ColorFromParent;
            break;
          case "material":
            source = ObjectColorSource.ColorFromMaterial;
            break;
        }
      }

      foreach (string objectId in colorProxy.objects)
      {
        Color convertedColor = Color.FromArgb(colorProxy.value);
        if (!ObjectColorsIdMap.TryGetValue(objectId, out (Color, ObjectColorSource) _))
        {
          ObjectColorsIdMap.Add(objectId, (convertedColor, source));
        }
      }
    }
  }
}
