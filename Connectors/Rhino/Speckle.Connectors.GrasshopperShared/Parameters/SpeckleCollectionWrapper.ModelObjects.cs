#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleCollectionWrapperGoo : GH_Goo<SpeckleCollectionWrapper>, ISpeckleGoo //, IGH_PreviewData // can be made previewable later
{
  private bool CastFromModelLayer(object source)
  {
    if (source is ModelLayer modelLayer)
    {
      Collection modelCollection =
        new()
        {
          name = modelLayer.Name,
          elements = new(),
          applicationId = modelLayer.Id?.ToString()
        };

      // get color and material
      Color? layerColor = null;
      if (modelLayer.DisplayColor is ModelColor color)
      {
        layerColor = Color.FromArgb(color.ToArgb());
      }

      SpeckleMaterialWrapper? layerMaterial = null;
      if (modelLayer.Material.Id is Guid id)
      {
        var mat = RhinoDoc.ActiveDoc.RenderMaterials.Find(id);
        SpeckleMaterialWrapperGoo materialGoo = new();
        materialGoo.CastFrom(mat);
        layerMaterial = materialGoo.Value;
      }

      Value = new SpeckleCollectionWrapper(modelCollection, GetModelLayerPath(modelLayer), layerColor, layerMaterial);
      return true;
    }

    return false;
  }

  private List<string> GetModelLayerPath(ModelLayer modellayer)
  {
    ModelContentName currentParent = modellayer.Parent;
    ModelContentName stem = modellayer.Parent.Stem;
    List<string> path = new() { modellayer.Name };
    while (currentParent != stem)
    {
      path.Add(currentParent);
      currentParent = currentParent.Parent;
    }
    path.Add(stem);

    path.Reverse();
    return path;
  }
}

#endif
