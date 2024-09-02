using Rhino;
using Rhino.DocObjects;
using Speckle.Connectors.Rhino.Extensions;
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

  /// <summary>
  /// Processes an object's color and adds the object id to a color proxy in <see cref="ColorProxies"/> if object color is set ByColor, ByMaterial, or ByParent.
  /// </summary>
  /// <param name="objId"></param>
  /// <param name="color"></param>
  private void ProcessObjectColor(string objId, Color color, ObjectColorSource source, int? materialIndex = null)
  {
    switch (source)
    {
      case ObjectColorSource.ColorFromObject:
      case ObjectColorSource.ColorFromParent: // will set definition objects to their instance's color settings, and top-level objects will treat this as if by layer.
        AddObjectIdToColorProxy(objId, color, source);
        break;
      case ObjectColorSource.ColorFromMaterial:
        if (materialIndex is int materialIndexInt && RhinoDoc.ActiveDoc.Materials.Count > materialIndexInt)
        {
          AddObjectIdToColorProxy(objId, RhinoDoc.ActiveDoc.Materials[materialIndexInt].DiffuseColor, source);
        }
        break;
      case ObjectColorSource.ColorFromLayer: // skipping by layer since this is the default receive option in rhino, and commit is structured by layers.
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
    // POC: in order to have high-fidelity color props, we need to send the source somewhere. Currently this is attached to the color proxy, but have discussed sending it as a separate proxy or as an property on the atomic object. TBD if this is the best place for it.
    string speckleSource = "object";
    switch (source)
    {
      case ObjectColorSource.ColorFromParent:
        speckleSource = "block";
        break;
      case ObjectColorSource.ColorFromLayer:
        speckleSource = "layer";
        break;
      case ObjectColorSource.ColorFromMaterial:
        speckleSource = "material";
        break;
    }

    colorProxy["source"] = speckleSource;
    return colorProxy;
  }

  /// <summary>
  /// Iterates through a given set of rhino objects and layers to collect colors.
  /// </summary>
  /// <param name="atomicObjects">atomic root objects, including instance objects</param>
  /// <param name="layers">layers used by atomic objects</param>
  /// <returns></returns>
  public List<ColorProxy> UnpackColors(List<RhinoObject> atomicObjects, List<Layer> layers)
  {
    // Stage 1: unpack colors from objects
    foreach (RhinoObject rootObj in atomicObjects)
    {
      ProcessObjectColor(
        rootObj.Id.ToString(),
        rootObj.Attributes.ObjectColor,
        rootObj.Attributes.ColorSource,
        rootObj.Attributes.MaterialIndex
      );
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (Layer layer in layers)
    {
      ProcessObjectColor(layer.Id.ToString(), layer.Color, ObjectColorSource.ColorFromObject);
    }

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
