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
  //POC: hack in place to create unique render material app ids since we are faking them from color + transparency
  // Do NOT try to access transparency.Alpha without checking if the transparency IsByAlpha (will throw if not)
  private string GetMaterialId(AutocadColor color, Transparency transparency) =>
    transparency.IsByAlpha ? $"{color.ColorIndex}-{Convert.ToDouble(transparency.Alpha)}" : $"{color.ColorIndex}";

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

    // do NOT access transparency.Alpha without checking IsByAlpha first, will throw
    double opacity = transparency.IsByAlpha ? Convert.ToDouble(transparency.Alpha) / 255.0 : 1;

    return new SpeckleRenderMaterial()
    {
      diffuse = autocadColor.ColorValue.ToArgb(),
      opacity = opacity,
      name = renderMaterialName,
      applicationId = GetMaterialId(autocadColor, transparency)
    };
  }

  public (AutocadColor, Transparency?) ConvertRenderMaterialToColorAndTransparency(RenderMaterial material)
  {
    System.Drawing.Color systemColor = System.Drawing.Color.FromArgb(material.diffuse);
    AutocadColor color = AutocadColor.FromColor(systemColor);

    // only create transparency if render material is not opaque
    Transparency? transparency = null;
    if (material.opacity != 1)
    {
      var alpha = (byte)(material.opacity * 255d);
      transparency = new Transparency(alpha);
    }

    return (color, transparency);
  }

  public List<RenderMaterialProxy> UnpackRenderMaterial(
    List<AutocadRootObject> rootObjects,
    List<LayerTableRecord> layers
  )
  {
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = new();

    // Stage 1: unpack materials from objects
    foreach (AutocadRootObject rootObj in rootObjects)
    {
      Entity entity = rootObj.Root;
      if (entity.EntityColor.IsByColor || entity.EntityColor.IsByAci)
      {
        string materialId = GetMaterialId(entity.Color, entity.Transparency);
        if (renderMaterialProxies.TryGetValue(materialId, out RenderMaterialProxy value))
        {
          value.objects.Add(rootObj.ApplicationId);
        }
        else
        {
          SpeckleRenderMaterial objMaterial = ConvertColorAndTransparencyToRenderMaterial(
            entity.Color,
            entity.Transparency
          );

          renderMaterialProxies[materialId] = new RenderMaterialProxy()
          {
            value = objMaterial,
            objects = [rootObj.ApplicationId]
          };
        }
      }
    }

    // Stage 2: make sure we collect layer materials as well
    foreach (LayerTableRecord layer in layers)
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
