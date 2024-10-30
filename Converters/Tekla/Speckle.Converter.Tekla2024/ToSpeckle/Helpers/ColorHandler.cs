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
    switch (modelObject)
    {
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

      case TSM.Part part:
        StoreByClassColor(part.Class, part.GetSpeckleApplicationId());
        break;
    }
  }

  private void StoreByClassColor(string classString, string objId) { }

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

  private void MakeAndCacheColorProxy(int color, string name, string colorId, string objId)
  {
    ColorProxy colorProxy =
      new()
      {
        name = name,
        value = color,
        objects = new() { objId },
        applicationId = colorId
      };

    ColorProxiesCache.Add(colorId, colorProxy);
  }
}
