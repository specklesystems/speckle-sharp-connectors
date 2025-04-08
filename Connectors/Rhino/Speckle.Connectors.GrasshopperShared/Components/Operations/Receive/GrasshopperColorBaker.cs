using Rhino.DocObjects;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

internal sealed class GrasshopperColorBaker
{
  // stores map of object id to color and color source
  public Dictionary<string, (Color, ObjectColorSource)> Cache { get; private set; } = new();

  public GrasshopperColorBaker(RootObjectUnpackerResult root)
  {
    if (root.ColorProxies != null)
    {
      ParseColors(root.ColorProxies);
    }
  }

  /// <summary>
  /// Parse Color Proxies and stores in ObjectColorsIdMap the relationship between object ids and colors
  /// </summary>
  /// <param name="colorProxies"></param>
  private void ParseColors(IReadOnlyCollection<ColorProxy> colorProxies)
  {
    foreach (ColorProxy colorProxy in colorProxies)
    {
      try
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
          if (!Cache.TryGetValue(objectId, out (Color, ObjectColorSource) _))
          {
            Cache.Add(objectId, (convertedColor, source));
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // TODO: add error
      }
    }
  }
}
