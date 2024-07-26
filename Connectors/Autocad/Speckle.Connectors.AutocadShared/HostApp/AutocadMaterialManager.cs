using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Objects.Other;
using Speckle.Connectors.Autocad.Operations.Send;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;
using SpeckleRenderMaterial = Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Utility class managing material creation and/or extraction from autocad. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class AutocadMaterialManager
{
  //POC: hack in place to create unique render mateiral app ids since we are faking them from color + transparency
  private string GetMaterialId(AutocadColor color, Transparency transparency) =>
    $"{color.ColorIndex}-{Convert.ToDouble(transparency.Alpha)}";

  // converts an autocad color to a render material
  // more info on autocad colors: https://gohtx.com/acadcolors.php
  // POC: we will send these as part of display styles in the future, but faking the color as a render material for now
  private SpeckleRenderMaterial ConvertColorAndTransparencyToRenderMaterial(
    AutocadColor autocadColor,
    Transparency transparency
  )
  {
    string renderMaterialName = autocadColor.HasColorName
      ? autocadColor.ColorName
      : autocadColor.HasBookName
        ? autocadColor.BookName
        : autocadColor.ColorNameForDisplay;

    return new SpeckleRenderMaterial()
    {
      diffuse = autocadColor.ColorValue.ToArgb(),
      opacity = Convert.ToDouble(transparency.Alpha) / 255.0,
      name = renderMaterialName,
      applicationId = GetMaterialId(autocadColor, transparency)
    };
  }

  public List<RenderMaterialProxy> UnpackRenderMaterial(
    List<AutocadRootObject> atomicObjects,
    Dictionary<string, LayerTableRecord> layerCache
  )
  {
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = new();

    // Stage 1: unpack materials from objects
    foreach (AutocadRootObject autocadRootObject in atomicObjects)
    {
      if (autocadRootObject.Root is Entity entity && entity.EntityColor.IsByColor)
      {
        string colorId = entity.Color.ColorIndex.ToString();
        if (renderMaterialProxies.TryGetValue(colorId, out RenderMaterialProxy value))
        {
          value.objects.Add(autocadRootObject.ApplicationId);
        }
        else
        {
          SpeckleRenderMaterial objMaterial = ConvertColorAndTransparencyToRenderMaterial(
            entity.Color,
            entity.Transparency
          );

          renderMaterialProxies[colorId] = new RenderMaterialProxy()
          {
            value = objMaterial,
            objects = [autocadRootObject.ApplicationId]
          };
        }
      }
    }

    // Stage 2: make sure we collect layer materials as well
    foreach (LayerTableRecord layer in layerCache.Values)
    {
      string materialId = GetMaterialId(layer.Color, layer.Transparency);

      if (renderMaterialProxies.TryGetValue(materialId, out RenderMaterialProxy value))
      {
        value.objects.Add(layer.Id.ToString());
      }
      else
      {
        renderMaterialProxies[materialId] = new RenderMaterialProxy()
        {
          value = ConvertColorAndTransparencyToRenderMaterial(layer.Color, layer.Transparency),
          objects = [layer.Id.ToString()]
        };
      }
    }

    return renderMaterialProxies.Values.ToList();
  }
}
