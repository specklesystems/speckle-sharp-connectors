using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

internal sealed class GrasshopperColorPacker
{
  // stores map of color id to color proxy
  public Dictionary<string, ColorProxy> ColorProxies { get; } = new();

  public GrasshopperColorPacker() { }

  /// <summary>
  /// Processes an object or collections's color and adds the id to a color proxy in <see cref="ColorProxies"/>
  /// </summary>
  /// <param name="objId"></param>
  /// <param name="color"></param>
  public void ProcessColor(string? objId, Color? color)
  {
    if (color is not Color c || objId is not string id)
    {
      return;
    }

    AddObjectIdToColorProxy(id, c);
  }

  private void AddObjectIdToColorProxy(string objectId, Color color)
  {
    string colorId = color.GetSpeckleApplicationId();
    if (ColorProxies.TryGetValue(colorId, out ColorProxy? proxy))
    {
      proxy.objects.Add(objectId);
    }
    else
    {
      ColorProxy newColor = ConvertColorToColorProxy(color, colorId);
      newColor.objects.Add(objectId);
      ColorProxies[colorId] = newColor;
    }
  }

  private ColorProxy ConvertColorToColorProxy(Color color, string id)
  {
    int argb = color.ToArgb();
    string? name = color.IsNamedColor ? color.Name : null;

    ColorProxy colorProxy =
      new()
      {
        value = argb,
        applicationId = id,
        name = name,
        objects = new()
      };

    return colorProxy;
  }
}
