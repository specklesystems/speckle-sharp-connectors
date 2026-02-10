using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Material = Autodesk.AutoCAD.DatabaseServices.Material;
using RenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadMaterialUnpacker
{
  private readonly ILogger<AutocadMaterialUnpacker> _logger;

  public AutocadMaterialUnpacker(ILogger<AutocadMaterialUnpacker> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Iterates through a given set of autocad objects and collects their materials. Note: expects objects to be "atomic", and extracted out of their instances already.
  /// </summary>
  /// <param name="unpackedAutocadObjects"></param>
  /// <param name="layers"></param>
  /// <returns></returns>
  public List<RenderMaterialProxy> UnpackMaterials(
    List<AutocadRootObject> unpackedAutocadObjects,
    List<LayerTableRecord> layers
  )
  {
    Dictionary<string, RenderMaterialProxy> materialProxies = new();
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    // Stage 1: unpack materials from objects
    foreach (AutocadRootObject rootObj in unpackedAutocadObjects)
    {
      try
      {
        Entity entity = rootObj.Root;

        // skip inherited materials
        if (entity.Material == "ByLayer" || entity.Material == "ByBlock")
        {
          continue;
        }

        if (transaction.GetObject(entity.MaterialId, OpenMode.ForRead) is Material material)
        {
          // skip default material
          if (material.Name == "Global")
          {
            continue;
          }

          string materialId = material.GetSpeckleApplicationId();
          if (materialProxies.TryGetValue(materialId, out RenderMaterialProxy? value))
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
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack render material from Autocad Entity");
      }
    }

    // Stage 2: make sure we collect layer materials as well
    foreach (LayerTableRecord layer in layers)
    {
      try
      {
        if (transaction.GetObject(layer.MaterialId, OpenMode.ForRead) is Material material)
        {
          // skip default material
          if (material.Name == "Global")
          {
            continue;
          }

          string materialId = material.GetSpeckleApplicationId();
          string layerId = layer.GetSpeckleApplicationId(); // Do not use handle directly, see note in the 'GetSpeckleApplicationId' method
          if (materialProxies.TryGetValue(materialId, out RenderMaterialProxy? value))
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
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack render material from Autocad Layer");
      }
    }

    transaction.Commit();
    return materialProxies.Values.ToList();
  }

  private RenderMaterialProxy ConvertMaterialToRenderMaterialProxy(Material material, string id)
  {
    EntityColor diffuseColor = material.Diffuse.Color.Color;
    System.Drawing.Color diffuse = System.Drawing.Color.FromArgb(
      diffuseColor.Red,
      diffuseColor.Green,
      diffuseColor.Blue
    );

    RenderMaterial renderMaterial =
      new()
      {
        name = material.Name,
        opacity = material.Opacity.Percentage,
        diffuse = diffuse.ToArgb(),
        applicationId = id
      };

    // Add additional properties
    renderMaterial["ior"] = material.Refraction.Index;
    renderMaterial["reflectivity"] = material.Reflectivity;

    return new RenderMaterialProxy()
    {
      value = renderMaterial,
      objects = new(),
      applicationId = id
    };
  }
}
