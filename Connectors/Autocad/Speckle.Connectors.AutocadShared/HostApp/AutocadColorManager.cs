using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Core.Models.Proxies;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadColorManager
{
  private readonly AutocadContext _autocadContext;

  // POC: Will be addressed to move it into AutocadContext!
  private Document Doc => Application.DocumentManager.MdiActiveDocument;

  public AutocadColorManager(AutocadContext autocadContext)
  {
    _autocadContext = autocadContext;
  }

  private ColorProxy ConvertColorToColorProxy(AutocadColor color, string id)
  {
    int argb = color.ColorValue.ToArgb();
    string name = color.ColorNameForDisplay;

    ColorProxy colorProxy = new(argb, id, name);

    if (color.IsByAci)
    {
      colorProxy["autocadColorIndex"] = color.ColorIndex;
    }

    return colorProxy;
  }

  public List<ColorProxy> UnpackColors(List<AutocadRootObject> rootObjects, List<LayerTableRecord> layers)
  {
    Dictionary<string, ColorProxy> colorProxies = new();

    // Stage 1: unpack materials from objects
    foreach (AutocadRootObject rootObj in rootObjects)
    {
      Entity entity = rootObj.Root;

      // skip any objects that inherit their colors
      if (!entity.Color.IsByAci || !entity.Color.IsByColor)
      {
        continue;
      }

      // assumes color names are unique
      string colorId = entity.Color.ColorName;

      if (colorProxies.TryGetValue(colorId, out ColorProxy value))
      {
        value.objects.Add(rootObj.ApplicationId);
      }
      else
      {
        colorProxies[colorId] = ConvertColorToColorProxy(entity.Color, colorId);
      }
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      // assumes color names are unique
      string colorId = layer.Color.ColorName;

      if (colorProxies.TryGetValue(colorId, out ColorProxy value))
      {
        value.objects.Add(layer.Id.ToString());
      }
      else
      {
        colorProxies[colorId] = ConvertColorToColorProxy(layer.Color, colorId);
      }
    }

    return colorProxies.Values.ToList();
  }

  public AutocadColor ConvertColorProxyToColor(ColorProxy colorProxy)
  {
    AutocadColor color = colorProxy["autocadColorIndex"] is short index
      ? AutocadColor.FromColorIndex(ColorMethod.ByAci, index)
      : AutocadColor.FromColor(System.Drawing.Color.FromArgb(colorProxy.value));

    return color;
  }
}
