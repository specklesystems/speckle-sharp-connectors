using Microsoft.Extensions.Logging;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Material = Rhino.DocObjects.Material;
using PhysicallyBasedMaterial = Rhino.DocObjects.PhysicallyBasedMaterial;
using RenderMaterial = Rhino.Render.RenderMaterial;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoMaterialUnpacker
{
  private readonly ILogger<RhinoMaterialUnpacker> _logger;

  public RhinoMaterialUnpacker(ILogger<RhinoMaterialUnpacker> logger)
  {
    _logger = logger;
  }

  public List<RenderMaterialProxy> UnpackRenderMaterial(List<RhinoObject> atomicObjects)
  {
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = new();
    Dictionary<string, Layer> usedLayerMap = new();

    // Stage 1: unpack materials from objects, and collect their uniqe layers in the process
    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      try
      {
        var layer = RhinoDoc.ActiveDoc.Layers[rhinoObject.Attributes.LayerIndex];
        usedLayerMap[layer.Id.ToString()] = layer;

        if (rhinoObject.Attributes.MaterialSource != ObjectMaterialSource.MaterialFromObject)
        {
          continue; // TODO: will not catch layer materials
        }

        var rhinoRenderMaterial = rhinoObject.GetRenderMaterial(true);
        var rhinoMaterial = rhinoObject.GetMaterial(true);
        var rhinoMaterialId = rhinoRenderMaterial?.Id.ToString() ?? rhinoMaterial?.Id.ToString();

        if (rhinoMaterialId == null)
        {
          continue;
        }

        if (renderMaterialProxies.TryGetValue(rhinoMaterialId, out RenderMaterialProxy? value))
        {
          value.objects.Add(rhinoObject.Id.ToString());
        }
        else
        {
          // TY Rhino api for being a bit confused about materials 💖
          SpeckleRenderMaterial? myMaterial = null;
          if (rhinoRenderMaterial is not null)
          {
            myMaterial = ConvertRenderMaterialToSpeckle(rhinoRenderMaterial);
          }
          else if (rhinoMaterial is not null)
          {
            RenderMaterial convertedRender = ConvertMaterialToRenderMaterial(rhinoMaterial);
            myMaterial = ConvertRenderMaterialToSpeckle(convertedRender);
          }

          if (myMaterial is not null)
          {
            renderMaterialProxies[rhinoMaterialId] = new RenderMaterialProxy()
            {
              value = myMaterial,
              objects = [rhinoObject.Id.ToString()]
            };
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack render material from RhinoObject");
      }
    }

    // Stage 2: make sure we collect layer materials as well
    foreach (var layer in usedLayerMap.Values)
    {
      try
      {
        // 1. Try to create from RenderMaterial
        var renderMaterial = layer.RenderMaterial;
        if (renderMaterial is not null)
        {
          if (renderMaterialProxies.TryGetValue(renderMaterial.Id.ToString(), out RenderMaterialProxy? value))
          {
            value.objects.Add(layer.Id.ToString());
          }
          else
          {
            renderMaterialProxies[renderMaterial.Id.ToString()] = new RenderMaterialProxy()
            {
              value = ConvertRenderMaterialToSpeckle(renderMaterial),
              objects = [layer.Id.ToString()]
            };
          }
        }

        // 2. As fallback, try to create from index
        // ON RECEIVE: when creating a layer on receive we cannot set RenderMaterial to layer. (RhinoCommon API limitation, it tested)
        // We can only set render material index of layer. So for the second send, we also need to check its index!
        var renderMaterialIndex = layer.RenderMaterialIndex;
        if (renderMaterialIndex == -1)
        {
          continue;
        }

        var renderMaterialFromIndex = RhinoDoc.ActiveDoc.Materials[renderMaterialIndex];
        if (renderMaterialFromIndex is null)
        {
          continue;
        }

        if (
          renderMaterialProxies.TryGetValue(
            renderMaterialFromIndex.Id.ToString(),
            out RenderMaterialProxy? renderMaterialProxy
          )
        )
        {
          renderMaterialProxy.objects.Add(layer.Id.ToString());
        }
        else
        {
          renderMaterialProxies[renderMaterialFromIndex.Id.ToString()] = new RenderMaterialProxy()
          {
            value = ConvertRenderMaterialToSpeckle(ConvertMaterialToRenderMaterial(renderMaterialFromIndex)),
            objects = [layer.Id.ToString()]
          };
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack render material from Rhino Layer");
      }
    }

    return renderMaterialProxies.Values.ToList();
  }

  // converts a rhino material to a rhino render material
  private RenderMaterial ConvertMaterialToRenderMaterial(Material material)
  {
    // get physically based render material
    Material pbMaterial = material;
    if (!material.IsPhysicallyBased)
    {
      pbMaterial = new();
      pbMaterial.CopyFrom(material);
      pbMaterial.ToPhysicallyBased();
    }

    return RenderMaterial.FromMaterial(pbMaterial, null);
  }

  private SpeckleRenderMaterial ConvertRenderMaterialToSpeckle(RenderMaterial renderMaterial)
  {
    PhysicallyBasedMaterial pbRenderMaterial = renderMaterial.ConvertToPhysicallyBased(
      RenderTexture.TextureGeneration.Allow
    );

    // get opacity
    // POC: pbr will return opacity = 0 for these because they are not pbr materials, they are transparent materials with IOR. Currently hardcoding 0.2 value in lieu of proper type support in rhino.
    double opacity = (renderMaterial.SmellsLikeGem || renderMaterial.SmellsLikeGlass) ? 0.2 : pbRenderMaterial.Opacity;

    string renderMaterialName = renderMaterial.Name ?? "default"; // default rhino material has no name
    Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    Color emissive = renderMaterial.TypeName.Equals("Emission")
      ? pbRenderMaterial.Material.EmissionColor
      : pbRenderMaterial.Emission.AsSystemColor(); // pbRenderMaterial.emission gives wrong color for emission materials, and material.emissioncolor gives the wrong value for most others *shrug*

    SpeckleRenderMaterial speckleRenderMaterial =
      new(opacity, pbRenderMaterial.Metallic, pbRenderMaterial.Roughness, diffuse, emissive)
      {
        name = renderMaterialName,
        applicationId = renderMaterial.Id.ToString()
      };

    // add additional dynamic props for rhino material receive
    speckleRenderMaterial["typeName"] = renderMaterial.TypeName;
    speckleRenderMaterial["ior"] = pbRenderMaterial.Material.IndexOfRefraction;
    speckleRenderMaterial["shine"] = pbRenderMaterial.Material.Shine;

    return speckleRenderMaterial;
  }
}
