#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;
using Grasshopper.Rhinoceros.Model;
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

      // get color
      Color? layerColor = null;
      if (modelLayer.DisplayColor is ModelColor color)
      {
        layerColor = Color.FromArgb(color.ToArgb());
      }

      Value = new SpeckleCollectionWrapper(modelCollection, GetModelLayerPath(modelLayer), layerColor);
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
