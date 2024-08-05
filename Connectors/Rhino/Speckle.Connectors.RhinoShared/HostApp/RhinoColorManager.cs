using Rhino.DocObjects;
using Speckle.Core.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing colors on objects and layers. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoColorManager
{
  public Dictionary<string, Color> ObjectColorsIdMap { get; } = new();

  public List<ColorProxy> UnpackColors(List<RhinoObject> atomicObjects, List<Layer> layers)
  {
    Dictionary<string, ColorProxy> colorProxies = new();

    // Stage 1: unpack materials from objects
    foreach (RhinoObject rhinoObj in atomicObjects)
    {
      ObjectAttributes atts = rhinoObj.Attributes;

      // skip any objects that inherit their colors
      if (atts.ColorSource != ObjectColorSource.ColorFromObject)
      {
        continue;
      }

      // assumes color names are unique
      string colorId = atts.ObjectColor.Name;
      string objectId = rhinoObj.Id.ToString();
      if (colorProxies.TryGetValue(colorId, out ColorProxy value))
      {
        value.objects.Add(objectId);
      }
      else
      {
        ColorProxy newColor = new(atts.ObjectColor.ToArgb(), colorId, colorId);
        newColor.objects.Add(objectId);
        colorProxies[colorId] = newColor;
      }
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (Layer layer in layers)
    {
      // assumes color names are unique
      string colorId = layer.Color.Name;
      string layerId = layer.Id.ToString();
      if (colorProxies.TryGetValue(colorId, out ColorProxy value))
      {
        value.objects.Add(layerId);
      }
      else
      {
        ColorProxy newColor = new(layer.Color.ToArgb(), colorId, colorId) { objects = new() };
        newColor.objects.Add(layerId);
        colorProxies[colorId] = newColor;
      }
    }

    return colorProxies.Values.ToList();
  }

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorsIdMap the relationship between object ids and colors
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
        Color convertedColor = Color.FromArgb(colorProxy.value);
        if (!ObjectColorsIdMap.TryGetValue(objectId, out Color _))
        {
          ObjectColorsIdMap.Add(objectId, convertedColor);
        }
      }
    }
  }
}
