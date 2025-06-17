#if RHINO8_OR_GREATER
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using Grasshopper.Rhinoceros.Model;
using Rhino.DocObjects;
using Grasshopper.Rhinoceros.Render;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData
{
  public SpeckleObjectWrapperGoo(ModelObject mo)
  {
    CastFrom(mo);
  }

  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case ModelObject modelObject:
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
          foreach (var entry in modelObject.UserText)
          {
            propertyDict.Add(entry.Key, entry.Value);
          }

          modelConverted[Constants.PROPERTIES_PROP] = propertyDict;

          // get the object color and material
          Color? color = GetColorFromModelObject(modelObject);
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
              Color = color,
              Material = materialWrapper.Value,
              Properties = propertyGroup
            };

          Value = so;
          return true;
        }

        throw new InvalidOperationException(
          $"Could not retrieve geometry from Model Object {modelObject.ObjectType}. Did you forget to bake these objects in your document?"
        );

      default:
        return false;
    }
  }

  private bool CastToModelObject<T>(ref T target)
  {
    switch (target)
    {
      case ModelObject:
        // create attributes
        ObjectAttributes modelObjectAtts = new();
        CastTo(ref modelObjectAtts);

        // create model object
        ModelObject modelObject = new(RhinoDoc.ActiveDoc, modelObjectAtts, Value.GeometryBase);
        target = (T)(object)modelObject;
        return true;

      case ObjectAttributes:
        ObjectAttributes objectAtts = new() { Name = Value.Name };

        if (Value.Color is Color color)
        {
          objectAtts.ObjectColor = color;
          objectAtts.ColorSource = ObjectColorSource.ColorFromObject;
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

          objectAtts.RenderMaterial = renderMaterial;
          objectAtts.MaterialSource = ObjectMaterialSource.MaterialFromObject;
        }

        // POC: only set layer if it exists in the doc. Avoid baking during cast.
        if (Value.Parent is SpeckleCollectionWrapper collectionWrapper)
        {
          int layerIndex = collectionWrapper.GetLayerIndex();
          if (layerIndex != -1)
          {
            objectAtts.LayerIndex = layerIndex;
          }
        }

        // add props
        Value.Properties.AssignToObjectAttributes(objectAtts);

        target = (T)(object)objectAtts;
        return true;

      case ModelRenderMaterial:
        if (Value.Material is SpeckleMaterialWrapper matWrapper)
        {
          SpeckleMaterialWrapperGoo matWrapperGoo = new(matWrapper);
          ModelRenderMaterial modelMat = new();
          if (matWrapperGoo.CastTo(ref modelMat))
          {
            target = (T)(object)modelMat;
            return true;
          }
        }
        return false;

      case ModelLayer:
        if (Value.Parent is SpeckleCollectionWrapper collWrapper)
        {
          SpeckleCollectionWrapperGoo collWrapperGoo = new(collWrapper);
          ModelLayer modelLayer = new();
          if (collWrapperGoo.CastTo(ref modelLayer))
          {
            target = (T)(object)modelLayer;
            return true;
          }
        }
        return false;

      default:
        return false;
    }
  }

  private GeometryBase? GetGeometryFromModelObject(ModelObject modelObject) =>
    RhinoDoc.ActiveDoc.Objects.FindId(modelObject.Id ?? Guid.Empty)?.Geometry;

  private Color? GetColorFromModelObject(ModelObject modelObject)
  {
    // we need to retrieve the actual color by the color source (otherwise will return default color for anything other than by object)
    int? argb = null;
    switch (modelObject.Display.Color?.Source)
    {
      case ObjectColorSource.ColorFromLayer:
        argb = modelObject.Layer.DisplayColor?.ToArgb();
        break;
      case ObjectColorSource.ColorFromObject:
        argb = modelObject.Display.Color?.Color.ToArgb();
        break;
      case ObjectColorSource.ColorFromMaterial:
        Rhino.Render.RenderMaterial? mat = GetMaterialFromModelObject(modelObject);
        argb = mat?.ToMaterial(Rhino.Render.RenderTexture.TextureGeneration.Skip)?.DiffuseColor.ToArgb();
        break;
      default:
        break;
    }
    return argb is int validArgb ? Color.FromArgb(validArgb) : null;
  }

  private Rhino.Render.RenderMaterial? GetMaterialFromModelObject(ModelObject modelObject)
  {
    // we need to retrieve the actual material by the material source (otherwise will return default material for anything other than by object)
    Guid? matId = null;
    switch (modelObject.Render.Material?.Source)
    {
      case ObjectMaterialSource.MaterialFromLayer:
        matId = modelObject.Layer.Material.Id;
        break;
      case ObjectMaterialSource.MaterialFromObject:
        matId = modelObject.Render.Material?.Material?.Id;
        break;
      case ObjectMaterialSource.MaterialFromParent: // POC: too complicated for now
      default:
        break;
    }

    return matId is Guid validId ? RhinoDoc.ActiveDoc.RenderMaterials.Find(validId) : null;
  }

  private bool TryCastToExtrusion<T>(ref T target)
  {
    Extrusion? extrusion = null;
    if (GH_Convert.ToExtrusion(Value.GeometryBase, ref extrusion, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Extrusion(extrusion);
      return true;
    }
    return false;
  }

  private bool TryCastToPointcloud<T>(ref T target)
  {
    PointCloud? pointCloud = null;
    if (GH_Convert.ToPointCloud(Value.GeometryBase, ref pointCloud, GH_Conversion.Both))
    {
      target = (T)(object)new GH_PointCloud(pointCloud);
      return true;
    }
    return false;
  }

  private bool TryCastToHatch<T>(ref T target)
  {
    Hatch? hatch = null;
    if (GH_Convert.ToHatch(Value.GeometryBase, ref hatch, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Hatch(hatch);
      return true;
    }
    return false;
  }

  private bool TryCastToSubD<T>(ref T target)
  {
    SubD? subd = null;
    if (GH_Convert.ToSubD(Value.GeometryBase, ref subd, GH_Conversion.Both))
    {
      target = (T)(object)new GH_SubD(subd);
      return true;
    }
    return false;
  }
}
#endif
