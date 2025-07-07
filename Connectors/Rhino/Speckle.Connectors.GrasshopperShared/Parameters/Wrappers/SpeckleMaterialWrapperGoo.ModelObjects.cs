#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Render;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Sdk;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a flat dictionary of (speckle property path, speckle property).
/// The speckle property path is the concatenated string of all original flattened keys with the property delimiter
/// </summary>
public partial class SpeckleMaterialWrapperGoo : GH_Goo<SpeckleMaterialWrapper>
{
  private bool CastFromModelRenderMaterial(object source)
  {
    switch (source)
    {
      case ModelObject modelObject:
        if (modelObject.Render.Material?.Material is ModelRenderMaterial modelRenderMaterial)
        {
          return CastFromModelRenderMaterial(modelRenderMaterial);
        }

        return false;

      case Rhino.Render.RenderMaterial renderMaterial:
        renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
        Value = new()
        {
          Base = ToSpeckleRenderMaterial(renderMaterial),
          Name = renderMaterial.Name,
          ApplicationId = renderMaterial.Id.ToString(),
          RhinoMaterial = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow),
          RhinoRenderMaterialId = renderMaterial.Id
        };

        return true;

      case ModelRenderMaterial modelMaterial:
        if (modelMaterial.Id is Guid id)
        {
          // this id can be the default render material id {defadefa-defa-defa-defa-defadefadefa} which tbh can't test for and will return null on find
          // assuming an id always exists and if failed to find, we'll use default.
          Rhino.Render.RenderMaterial matRenderMaterial =
            RhinoDoc.ActiveDoc.RenderMaterials.Find(id) ?? Material.DefaultMaterial.RenderMaterial;
          if (matRenderMaterial is null)
          {
            throw new SpeckleException($"Failed to find ModelRenderMaterial with guid: {id}");
          }

          return CastFromModelRenderMaterial(matRenderMaterial);
        }

        return false;
    }

    return false;
  }

  private bool CastToModelRenderMaterial<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(ModelRenderMaterial))
    {
      if (
        Value.RhinoRenderMaterialId is Guid matGuid
        && RhinoDoc.ActiveDoc.RenderMaterials.Find(matGuid) is RenderMaterial existingMat
      )
      {
        target = (T)(object)(new ModelRenderMaterial(existingMat));
        return true;
      }
      else
      {
        var atts = new ModelRenderMaterial.Attributes()
        {
          Name = Value.Name,
          RenderMaterial = RenderMaterial.CreateBasicMaterial(Value.RhinoMaterial, RhinoDoc.ActiveDoc)
        };

        target = (T)(object)(new ModelRenderMaterial(atts));
        return true;
      }
    }

    return false;
  }

  private SpeckleRenderMaterial ToSpeckleRenderMaterial(Rhino.Render.RenderMaterial mat)
  {
    Rhino.DocObjects.PhysicallyBasedMaterial pbRenderMaterial = mat.ConvertToPhysicallyBased(
      RenderTexture.TextureGeneration.Allow
    );

    // get opacity
    // POC: pbr will return opacity = 0 for these because they are not pbr materials, they are transparent materials with IOR. Currently hardcoding 0.2 value in lieu of proper type support in rhino.
    double opacity = (mat.SmellsLikeGem || mat.SmellsLikeGlass) ? 0.2 : pbRenderMaterial.Opacity;

    string renderMaterialName = mat.Name ?? "default"; // default rhino material has no name
    Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    Color emissive = mat.TypeName.Equals("Emission")
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
        applicationId = mat.Id.ToString(),
        ["typeName"] = mat.TypeName,
        ["ior"] = pbRenderMaterial.Material.IndexOfRefraction,
        ["shine"] = pbRenderMaterial.Material.Shine,
      };

    return speckleRenderMaterial;
  }
}

#endif
