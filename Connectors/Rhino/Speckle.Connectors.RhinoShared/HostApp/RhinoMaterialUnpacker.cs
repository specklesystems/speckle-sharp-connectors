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

  /// <summary>
  /// For send operations
  /// </summary>
  private Dictionary<string, RenderMaterialProxy> RenderMaterialProxies { get; } = new();

  public RhinoMaterialUnpacker(ILogger<RhinoMaterialUnpacker> logger)
  {
    _logger = logger;
  }

  /// <summary>
  /// Processes an object's material and adds the object id to a material proxy in <see cref="RenderMaterialProxies"/> if object color is set ByObject or ByParent.
  /// </summary>
  /// <param name="objId"></param>
  private void ProcessObjectMaterial(
    string objId,
    RenderMaterial? renderMaterial,
    Material? material,
    ObjectMaterialSource source
  )
  {
    switch (source)
    {
      case ObjectMaterialSource.MaterialFromObject:
        AddObjectIdToRenderMaterialProxy(objId, renderMaterial, material);
        break;

      // POC: skip if object material source is *not* by object. we don't support render material inheritance atm bc alex disagrees with the concept
      default:
        break;
    }
  }

  private void AddObjectIdToRenderMaterialProxy(string objectId, RenderMaterial? renderMaterial, Material? material)
  {
    // NOTE: material ids are not the same, even if the underlying material is. the number of materials in the mat table corresponds
    // with the number of objects, and each material will get a new id, even if it is THE SAME. shockingly, TY bob, material names
    // are unique so we use those for identity checks rather than the material's id.
    string? renderMaterialId = renderMaterial?.Name ?? material?.Name; //renderMaterial?.Id.ToString() ?? material?.Id.ToString();

    if (renderMaterialId is not null)
    {
      if (RenderMaterialProxies.TryGetValue(renderMaterialId, out RenderMaterialProxy? proxy))
      {
        proxy.objects.Add(objectId);
      }
      else
      {
        if (
          ConvertMaterialToRenderMaterialProxy(renderMaterialId, renderMaterial, material)
          is RenderMaterialProxy newRenderMaterial
        )
        {
          newRenderMaterial.objects.Add(objectId);
          RenderMaterialProxies[renderMaterialId] = newRenderMaterial;
        }
      }
    }
  }

  private RenderMaterialProxy? ConvertMaterialToRenderMaterialProxy(
    string materialId,
    RenderMaterial? renderMaterial,
    Material? material
  )
  {
    // TY Rhino api for being a bit confused about materials 💖
    SpeckleRenderMaterial? myMaterial = null;
    if (renderMaterial is not null)
    {
      myMaterial = ConvertRenderMaterialToSpeckle(renderMaterial);
    }
    else if (material is not null)
    {
      RenderMaterial? convertedRender = ConvertMaterialToRenderMaterial(material);
      if (convertedRender is not null)
      {
        myMaterial = ConvertRenderMaterialToSpeckle(convertedRender);
      }
    }

    if (myMaterial is null)
    {
      return null;
    }

    RenderMaterialProxy renderMaterialProxy =
      new()
      {
        value = myMaterial,
        applicationId = materialId,
        objects = new()
      };

    // POC: we are not attaching source information here, since we do not support material inheritance
    return renderMaterialProxy;
  }

  /// <summary>
  /// Iterates through a given set of rhino objects and layers to collect render materials.
  /// </summary>
  /// <param name="atomicObjects">atomic root objects, including instance objects</param>
  /// <param name="layers">the layers corresponding to collections on the root collection</param>
  /// <returns></returns>
  public List<RenderMaterialProxy> UnpackRenderMaterials(List<RhinoObject> atomicObjects, List<Layer> layers)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    // Stage 1: unpack materials from objects
    foreach (RhinoObject rootObj in atomicObjects)
    {
      if (rootObj.Attributes.MaterialSource == ObjectMaterialSource.MaterialFromLayer)
      {
        continue;
      }

      // materials are confusing in rhino. we need both render material and material because objects can have either assigned
      RenderMaterial? rhinoRenderMaterial = rootObj.GetRenderMaterial(true);
      Material? rhinoMaterial = rootObj.GetMaterial(true);

      try
      {
        ProcessObjectMaterial(
          rootObj.Id.ToString(),
          rhinoRenderMaterial,
          rhinoMaterial,
          rootObj.Attributes.MaterialSource
        );
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack material from Rhino Object");
      }
    }

    // Stage 2: make sure we collect layer materials as well
    foreach (Layer layer in layers)
    {
      // materials are confusing in rhino. we will first try to get layer render material and then material by index if null
      RenderMaterial? rhinoRenderMaterial = layer.RenderMaterial;
      Material? rhinoMaterial =
        layer.RenderMaterialIndex == -1 ? null : currentDoc.Materials[layer.RenderMaterialIndex];

      try
      {
        ProcessObjectMaterial(
          layer.Id.ToString(),
          rhinoRenderMaterial,
          rhinoMaterial,
          ObjectMaterialSource.MaterialFromObject
        );
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to unpack materials from Rhino Layer");
      }
    }

    return RenderMaterialProxies.Values.ToList();
  }

  // converts a rhino material to a rhino render material
  private RenderMaterial? ConvertMaterialToRenderMaterial(Material material)
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
      new()
      {
        name = renderMaterialName,
        opacity = opacity,
        metalness = pbRenderMaterial.Metallic,
        roughness = pbRenderMaterial.Roughness,
        diffuse = diffuse.ToArgb(),
        emissive = emissive.ToArgb(),
        applicationId = renderMaterial.Id.ToString()
      };

    // add additional dynamic props for rhino material receive
    speckleRenderMaterial["typeName"] = renderMaterial.TypeName;
    speckleRenderMaterial["ior"] = pbRenderMaterial.Material.IndexOfRefraction;
    speckleRenderMaterial["shine"] = pbRenderMaterial.Material.Shine;

    return speckleRenderMaterial;
  }
}
