using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Sdk;
using Speckle.Sdk.Models.Proxies;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadColorUnpacker
{
  private readonly ILogger<AutocadColorUnpacker> _logger;

  public AutocadColorUnpacker(ILogger<AutocadColorUnpacker> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// For send operations
  /// </summary>
  private Dictionary<string, ColorProxy> ColorProxies { get; } = new();

  /// <summary>
  /// Iterates through a given set of autocad objects and layers to collect colors.
  /// </summary>
  /// <param name="unpackedAutocadRootObjects">atomic root objects, including instance objects</param>
  /// <param name="layers">layers used by atomic objects</param>
  /// <returns></returns>
  public List<ColorProxy> UnpackColors(
    List<AutocadRootObject> unpackedAutocadRootObjects,
    List<LayerTableRecord> layers
  )
  {
    // Stage 1: unpack colors from objects
    foreach (AutocadRootObject rootObj in unpackedAutocadRootObjects)
    {
      try
      {
        Entity entity = rootObj.Root;
        ProcessObjectColor(rootObj.ApplicationId, entity.Color);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack colors from Autocad Entity");
      }
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      try
      {
        ProcessObjectColor(layer.GetSpeckleApplicationId(), layer.Color);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack colors from Autocad Layer");
      }
    }

    return ColorProxies.Values.ToList();
  }

  /// <summary>
  /// Processes an object's color and adds the object id to a color proxy in <see cref="ColorProxies"/> if object color is set ByAci, ByColor, or ByBlock.
  /// Skips processing ByPen for now, because I don't understand what this means.
  /// </summary>
  /// <param name="objectId"></param>
  /// <param name="color"></param>
  /// <remarks>Skips processing object colors if it is "ByLayer" since autocad commits are structured by layer and by default sets the color by layer on receive. If this ever changes, then we do need to start processing object colors by layer.</remarks>
  private void ProcessObjectColor(string objectId, AutocadColor color)
  {
    switch (color.ColorMethod)
    {
      case ColorMethod.ByAci:
      case ColorMethod.ByColor:
      case ColorMethod.ByBlock:
        AddObjectIdToColorProxy(objectId, color);
        break;
      case ColorMethod.ByLayer: // skipping these since autocad commits are structured by layer. Will need to be updated if this ever changes!!
      case ColorMethod.ByPen: // POC: no idea what this means
        break;
    }
  }

  private void AddObjectIdToColorProxy(string objectId, AutocadColor color)
  {
    string colorId = color.GetSpeckleApplicationId();
    if (ColorProxies.TryGetValue(colorId, out ColorProxy? proxy))
    {
      proxy.objects.Add(objectId);
    }
    else
    {
      ColorProxy newColor = ConvertColorToColorProxy(color);
      newColor.objects.Add(objectId);
      ColorProxies[colorId] = newColor;
    }
  }

  private ColorProxy ConvertColorToColorProxy(AutocadColor color)
  {
    int argb = color.ColorValue.ToArgb();
    string name = color.ColorNameForDisplay;
    string id = color.GetSpeckleApplicationId();

    ColorProxy colorProxy =
      new()
      {
        value = argb,
        applicationId = id,
        name = name,
        objects = new()
      };

    // add the color source as well for receiving in other apps
    // POC: in order to support full fidelity color support across autocad and rhino, we need to keep track of the color source property. Not sure if this is the best place to keep track of the source, vs on a ColorSourceProxy or as a property on the atomic object.
    colorProxy["source"] = color.IsByBlock
      ? "block"
      : color.IsByLayer
        ? "layer"
        : "object";

    // set additional properties if by aci or by block
    // ByBlock colors for some reason do not have their color value set to the correct color (white): instead it's a near-black
    // ByACI is an Autocad internal index for set rgb values, which effects name presentation, see: https://gohtx.com/acadcolors.php
    if (color.IsByAci)
    {
      colorProxy["autocadColorIndex"] = (int)color.ColorIndex;
    }
    else if (color.IsByBlock)
    {
      colorProxy.value = -1;
    }

    return colorProxy;
  }
}
