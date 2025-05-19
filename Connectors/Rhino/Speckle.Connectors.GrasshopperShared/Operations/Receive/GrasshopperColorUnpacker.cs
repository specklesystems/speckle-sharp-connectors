using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

internal sealed class GrasshopperColorUnpacker
{
  // stores map of object id to color and color source
  public Dictionary<string, Color> Cache { get; private set; } = new();

  public GrasshopperColorUnpacker(RootObjectUnpackerResult root)
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
        foreach (string objectId in colorProxy.objects)
        {
          Color convertedColor = Color.FromArgb(colorProxy.value);
          if (!Cache.TryGetValue(objectId, out Color _))
          {
            Cache.Add(objectId, convertedColor);
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
