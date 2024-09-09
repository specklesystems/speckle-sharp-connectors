using Rhino.DocObjects;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoColorBaker
{
  /// <summary>
  /// For receive operations
  /// </summary>
  public Dictionary<string, (Color, ObjectColorSource)> ObjectColorsIdMap { get; } = new();

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
