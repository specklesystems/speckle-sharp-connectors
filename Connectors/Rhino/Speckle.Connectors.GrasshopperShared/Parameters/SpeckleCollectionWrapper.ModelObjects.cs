#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleCollectionWrapperGoo : GH_Goo<SpeckleCollectionWrapper> //, IGH_PreviewData // can be made previewable later
{
  private bool CastToModelLayer<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(ModelLayer))
    {
      // create attributes
      ModelLayer.Attributes atts = new();
      CastTo<ModelLayer.Attributes>(ref atts);
      ModelLayer modelLayer = new(atts);
      target = (T)(object)modelLayer;
      return true;
    }

    if (type == typeof(ModelLayer.Attributes))
    {
      ModelContentName path = string.Join(ModelContentName.Separator, Value.Path);
      ModelLayer.Attributes atts = new() { Name = Value.Collection.name, Path = path };

      if (Value.Color is Color color)
      {
        atts.DisplayColor = color;
      }

      // POC: only set material if it exists in the doc. Avoiding baking during cast.
      if (
        Value.Material is SpeckleMaterialWrapper materialWrapper
        && materialWrapper.RhinoRenderMaterialId != Guid.Empty
      )
      {
        Rhino.Render.RenderMaterial renderMaterial = RhinoDoc.ActiveDoc.RenderMaterials.Find(
          materialWrapper.RhinoRenderMaterialId
        );

        atts.Material = renderMaterial;
      }

      target = (T)(object)atts;
      return true;
    }

    return false;
  }

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
      if (modelLayer.Material?.Id is Guid id)
      {
        var mat = RhinoDoc.ActiveDoc.RenderMaterials.Find(id);
        SpeckleMaterialWrapperGoo materialGoo = new();
        materialGoo.CastFrom(mat);
        layerMaterial = materialGoo.Value;
      }

      Value = new SpeckleCollectionWrapper()
      {
        Base = modelCollection,
        Name = modelLayer.Name,
        Color = layerColor,
        Material = layerMaterial,
        Path = GetModelLayerPath(modelLayer)
      };

      return true;
    }

    return false;
  }

  private List<string> GetModelLayerPath(ModelLayer modellayer) => modellayer.Path.Split().ToList();
}

#endif
