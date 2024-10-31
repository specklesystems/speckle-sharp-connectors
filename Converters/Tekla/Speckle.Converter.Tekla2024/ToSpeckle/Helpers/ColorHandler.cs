using Speckle.Converter.Tekla2024.Extensions;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Converter.Tekla2024.ToSpeckle.Helpers;

public class ColorHandler
{
  public Dictionary<string, ColorProxy> ColorProxiesCache { get; } = new();

  private readonly IConverterSettingsStore<TeklaConversionSettings> _settingsStore;

  public ColorHandler(IConverterSettingsStore<TeklaConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public void ProcessColor(TSM.ModelObject modelObject)
  {
    var color = new TSMUI.Color();
    TSMUI.ModelObjectVisualization.GetRepresentation(modelObject, ref color);
    int r = (int)(color.Red * 255);
    int g = (int)(color.Green * 255);
    int b = (int)(color.Blue * 255);
    int a = (int)(color.Transparency * 255);
    int argb = (a << 24) | (r << 16) | (g << 8) | b;

    MakeAndCacheColorProxy(
      argb,
      color.ToString(),
      color.GetSpeckleApplicationId(),
      modelObject.GetSpeckleApplicationId()
    );

    /*
    switch (modelObject) {
      
      case TSM.ControlArc arc:
        StoreByControlColor(arc.Color.ToString(), arc.Color.GetSpeckleApplicationId(), arc.GetSpeckleApplicationId());
        break;
      case TSM.ControlCircle circle:
        StoreByControlColor(
          circle.Color.ToString(),
          circle.Color.GetSpeckleApplicationId(),
          circle.GetSpeckleApplicationId()
        );
        break;
      case TSM.ControlLine line:
        StoreByControlColor(
          line.Color.ToString(),
          line.Color.GetSpeckleApplicationId(),
          line.GetSpeckleApplicationId()
        );
        break;
      case TSM.ControlPolycurve polycurve:
        StoreByControlColor(
          polycurve.Color.ToString(),
          polycurve.Color.GetSpeckleApplicationId(),
          polycurve.GetSpeckleApplicationId()
        );
        break;
      */
  }

  /*
    // We are using the enum string because different control geometry have different enums sigh.
    // eg, arc color enums are of type ControlObjectColorEnum, while circle color enums are of type ControlCircleColorEnum
    private void StoreByControlColor(string color, string colorId, string objId)
    {
      if (ColorProxiesCache.TryGetValue(colorId, out ColorProxy colorProxy))
      {
        colorProxy.objects.Add(objId);
      }
      else
      {
        // create the color proxy
        int colorValue = color switch
        {
          "BLACK" => System.Drawing.Color.Black.ToArgb(),
          "BLUE" => System.Drawing.Color.Blue.ToArgb(),
          "CYAN" => System.Drawing.Color.Cyan.ToArgb(),
          "GREEN" => System.Drawing.Color.Green.ToArgb(),
          "MAGENTA" => System.Drawing.Color.Magenta.ToArgb(),
          "RED" => System.Drawing.Color.Red.ToArgb(),
          "WHITE" => System.Drawing.Color.White.ToArgb(),
          "YELLOW" => System.Drawing.Color.Yellow.ToArgb(),
          "YELLOW_RED" => System.Drawing.Color.Orange.ToArgb(),
          _ => System.Drawing.Color.Gray.ToArgb(),
        };
  
        MakeAndCacheColorProxy(colorValue, color, colorId, objId);
      }
    }
  */

  private void MakeAndCacheColorProxy(int color, string name, string colorId, string objId)
  {
    if (ColorProxiesCache.TryGetValue(colorId, out ColorProxy colorProxy))
    {
      colorProxy.objects.Add(objId);
    }
    else
    {
      ColorProxy newColorProxy =
        new()
        {
          name = name,
          value = color,
          objects = new() { objId },
          applicationId = colorId
        };

      ColorProxiesCache.Add(colorId, newColorProxy);
    }
  }
}
