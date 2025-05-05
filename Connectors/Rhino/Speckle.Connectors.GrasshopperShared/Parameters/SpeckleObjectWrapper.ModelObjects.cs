#if RHINO8_OR_GREATER
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Display;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;
using Rhino.DocObjects;
using Grasshopper.Rhinoceros.Render;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public SpeckleObjectWrapperGoo(ModelObject mo)
  {
    CastFrom(mo);
  }

  private bool CastToModelObject<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(ModelObject))
    {
      // create attributes
      ObjectAttributes atts = new();
      CastTo<ObjectAttributes>(ref atts);

      // create model object
      ModelObject modelObject = new(RhinoDoc.ActiveDoc, atts, Value.GeometryBase);
      target = (T)(object)modelObject;
      return true;
    }

    if (type == typeof(ObjectAttributes))
    {
      ObjectAttributes atts = new() { Name = Value.Name };

      if (Value.Color is Color color)
      {
        atts.ObjectColor = color;
        atts.ColorSource = ObjectColorSource.ColorFromObject;
      }

      // POC: only set material if it exists in the doc. Avoiding baking during cast.
      // ModelObject.Render.Material has no setter, so we are handling it here.
      if (
        Value.Material is SpeckleMaterialWrapper materialWrapper
        && materialWrapper.RhinoRenderMaterialId != Guid.Empty
      )
      {
        Rhino.Render.RenderMaterial renderMaterial = RhinoDoc.ActiveDoc.RenderMaterials.Find(
          materialWrapper.RhinoRenderMaterialId
        );

        atts.RenderMaterial = renderMaterial;
        atts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }

      // POC: only set layer if it exists in the doc. Avoid baking during cast.
      // ModelObject.Layer has no setter, so we are handling it here.
      if (Value.Parent is SpeckleCollectionWrapper collectionWrapper)
      {
        int layerIndex = collectionWrapper.GetLayerIndex();
        if (layerIndex != -1)
        {
          atts.LayerIndex = layerIndex;
        }
      }

      foreach (var kvp in Value.Properties.Value)
      {
        atts.SetUserString(kvp.Key, kvp.Value.Value.ToString());
      }

      target = (T)(object)atts;
      return true;
    }

    if (type == typeof(ModelRenderMaterial))
    {
      if (Value.Material is SpeckleMaterialWrapper matWrapper)
      {
        SpeckleMaterialWrapperGoo matWrapperGoo = new(matWrapper);
        ModelRenderMaterial modelMat = new();
        if (matWrapperGoo.CastTo<ModelRenderMaterial>(ref modelMat))
        {
          target = (T)(object)modelMat;
          return true;
        }
      }
    }

    if (type == typeof(ModelLayer))
    {
      if (Value.Parent is SpeckleCollectionWrapper collWrapper)
      {
        SpeckleCollectionWrapperGoo collWrapperGoo = new(collWrapper);
        ModelLayer modelLayer = new();
        if (collWrapperGoo.CastTo<ModelLayer>(ref modelLayer))
        {
          target = (T)(object)modelLayer;
          return true;
        }
      }
    }

    return false;
  }

  private bool CastFromModelObject(object source)
  {
    if (source is ModelObject modelObject)
    {
      if (GetGeometryFromModelObject(modelObject) is GeometryBase modelGB)
      {
        Base modelConverted = SpeckleConversionContext.ConvertToSpeckle(modelGB);
        SpecklePropertyGroupGoo propertyGroup = new();
        propertyGroup.CastFrom(modelObject.UserText);

        // get the object layer

        SpeckleCollectionWrapperGoo collWrapperGoo = new();
        SpeckleCollectionWrapper? collWrapper = collWrapperGoo.CastFrom(modelObject.Layer)
          ? collWrapperGoo.Value
          : null;

        // update the converted Base with props as well
        modelConverted.applicationId = modelObject.Id?.ToString();
        modelConverted[Constants.NAME_PROP] = modelObject.Name.ToString();
        Dictionary<string, object?> propertyDict = new();
        foreach (var entry in propertyGroup.Value)
        {
          propertyDict.Add(entry.Key, entry.Value.Value);
        }

        modelConverted[Constants.PROPERTIES_PROP] = propertyDict;

        // get the object color and material
        ObjectDisplayColor.Value? color = modelObject.Display.Color;
        SpeckleMaterialWrapperGoo? materialWrapper = new();
        if (GetMaterialFromModelObject(modelObject) is Rhino.Render.RenderMaterial renderMat)
        {
          materialWrapper.CastFrom(renderMat);
        }

        SpeckleObjectWrapper so =
          new()
          {
            GeometryBase = modelGB,
            Base = modelConverted,
            Parent = collWrapper,
            Name = modelObject.Name.ToString(),
            Color = color is null ? null : Color.FromArgb(color.Value.Color.ToArgb()),
            Material = materialWrapper.Value,
            Properties = propertyGroup,
            WrapperGuid = null // keep this null, processed on send
          };

        Value = so;
        return true;
      }
      else
      {
        throw new InvalidOperationException(
          $"Could not retrieve geometry from Model Object {modelObject.ObjectType}. Did you forget to bake these objects in your document?"
        );
      }
    }

    return false;
  }

  private GeometryBase? GetGeometryFromModelObject(ModelObject modelObject) =>
    RhinoDoc.ActiveDoc.Objects.FindId(modelObject.Id ?? Guid.Empty)?.Geometry;

  private Rhino.Render.RenderMaterial? GetMaterialFromModelObject(ModelObject modelObject) =>
    RhinoDoc.ActiveDoc.RenderMaterials.Find(modelObject.Render.Material?.Material?.Id ?? Guid.Empty);
}
#endif
