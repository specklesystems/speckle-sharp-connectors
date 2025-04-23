#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Render;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Sdk;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The Speckle Property Group Goo is a flat dictionary of (speckle property path, speckle property).
/// The speckle property path is the concatenated string of all original flattened keys with the property delimiter
/// </summary>
public partial class SpeckleMaterialWrapperGoo : GH_Goo<SpeckleMaterialWrapper>, ISpeckleGoo
{
  private bool CastFromModelRenderMaterial(object source)
  {
    switch (source)
    {
      case Rhino.Render.RenderMaterial renderMaterial:
        renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
        Value = new()
        {
          Base = ToSpeckleRenderMaterial(renderMaterial),
          RhinoMaterial = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow),
          RhinoRenderMaterialId = renderMaterial.Id
        };

        return true;
      case ModelRenderMaterial modelMaterial:
        if (modelMaterial.Id is Guid id)
        {
          // this id can be the default render material id {defadefa-defa-defa-defa-defadefadefa} which tbh can't test for and will return null on find
          // assuming an id always exists and if failed to find, we'll use default.
          Rhino.Render.RenderMaterial renderMaterial =
            RhinoDoc.ActiveDoc.RenderMaterials.Find(id) ?? Material.DefaultMaterial.RenderMaterial;

          if (renderMaterial is null)
          {
            throw new SpeckleException($"Failed to find ModelRenderMaterial with guid: {id}");
          }

          renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
          Value = new()
          {
            Base = ToSpeckleRenderMaterial(renderMaterial),
            RhinoMaterial = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow),
            RhinoRenderMaterialId = renderMaterial.Id
          };

          return true;
        }

        return false;
    }

    return false;
  }
}
#endif
