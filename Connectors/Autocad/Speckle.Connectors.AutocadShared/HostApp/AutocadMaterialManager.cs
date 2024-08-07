using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Objects.Other;
using Speckle.Connectors.Autocad.Operations.Send;
using AutocadColor = Autodesk.AutoCAD.Colors.Color;
using Material = Autodesk.AutoCAD.DatabaseServices.Material;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadMaterialManager
{
  private readonly AutocadContext _autocadContext;

  // POC: Will be addressed to move it into AutocadContext!
  private Document Doc => Application.DocumentManager.MdiActiveDocument;

  public Dictionary<string, (AutocadColor, Transparency?)> ObjectMaterialsIdMap { get; } = new();

  public AutocadMaterialManager(AutocadContext autocadContext)
  {
    _autocadContext = autocadContext;
  }

  private RenderMaterialProxy ConvertMaterialToRenderMaterialProxy(Material material, string id)
  {
    EntityColor diffuseColor = material.Diffuse.Color.Color;
    System.Drawing.Color diffuse = System.Drawing.Color.FromArgb(
      diffuseColor.Red,
      diffuseColor.Green,
      diffuseColor.Blue
    );
    string name = material.Name;
    double opacity = material.Opacity.Percentage;

    RenderMaterial renderMaterial = new(opacity: opacity, diffuse: diffuse) { name = name, applicationId = id };
    // Add additional properties
    renderMaterial["ior"] = material.Refraction.Index;
    renderMaterial["reflectivity"] = material.Reflectivity;

    RenderMaterialProxy materialProxy = new(renderMaterial, new());
    return materialProxy;
  }

  public List<RenderMaterialProxy> UnpackMaterials(List<AutocadRootObject> rootObjects, List<LayerTableRecord> layers)
  {
    Dictionary<string, RenderMaterialProxy> materialProxies = new();
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    // Stage 1: unpack materials from objects
    foreach (AutocadRootObject rootObj in rootObjects)
    {
      Entity entity = rootObj.Root;

      // skip inherited materials
      if (entity.Material == "ByLayer")
      {
        continue;
      }

      if (transaction.GetObject(entity.MaterialId, OpenMode.ForRead) is Material material)
      {
        string materialId = material.Handle.ToString();
        if (materialProxies.TryGetValue(materialId, out RenderMaterialProxy value))
        {
          value.objects.Add(rootObj.ApplicationId);
        }
        else
        {
          RenderMaterialProxy materialProxy = ConvertMaterialToRenderMaterialProxy(material, materialId);
          materialProxy.objects.Add(rootObj.ApplicationId);
          materialProxies[materialId] = materialProxy;
        }
      }
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      if (transaction.GetObject(layer.MaterialId, OpenMode.ForRead) is Material material)
      {
        string materialId = material.Handle.ToString();
        string layerId = layer.Handle.ToString();
        if (materialProxies.TryGetValue(materialId, out RenderMaterialProxy value))
        {
          value.objects.Add(layerId);
        }
        else
        {
          RenderMaterialProxy materialProxy = ConvertMaterialToRenderMaterialProxy(material, materialId);
          materialProxy.objects.Add(layerId);
          materialProxies[materialId] = materialProxy;
        }
      }
    }

    return materialProxies.Values.ToList();
  }

  /// <summary>
  /// Convert a Speckle render material to a color and transparency.
  /// </summary>
  /// <param name="material"></param>
  /// <returns></returns>
  /// <remarks>POC: we are not converting to a native material due to complexity in additional properties like Map and Scale.</remarks>
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

  public void ParseRenderMaterials(
    List<RenderMaterialProxy> materialProxies,
    Action<string, double?>? onOperationProgressed
  )
  {
    // keeps track of the object id to material index
    var count = 0;
    foreach (RenderMaterialProxy materialProxy in materialProxies)
    {
      onOperationProgressed?.Invoke("Converting render materials", (double)++count / materialProxies.Count);
      foreach (string objectId in materialProxy.objects)
      {
        (AutocadColor, Transparency?) convertedMaterial = ConvertRenderMaterialToColorAndTransparency(
          materialProxy.value
        );

        if (!ObjectMaterialsIdMap.ContainsKey(objectId))
        {
          ObjectMaterialsIdMap.Add(objectId, convertedMaterial);
        }
      }
    }
  }
}
