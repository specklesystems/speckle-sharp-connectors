#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Render;
using Rhino;
using Rhino.Render;

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
          RhinoMaterial = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow)
        };

        return true;
      case ModelRenderMaterial modelMaterial:
        if (modelMaterial.Id is Guid id)
        {
          Rhino.Render.RenderMaterial renderMaterial = RhinoDoc.ActiveDoc.RenderMaterials.Find(id);
          renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow);
          Value = new()
          {
            Base = ToSpeckleRenderMaterial(renderMaterial),
            RhinoMaterial = renderMaterial.ToMaterial(RenderTexture.TextureGeneration.Allow)
          };

          return true;
        }

        return false;
    }

    return false;
  }
}
#endif
