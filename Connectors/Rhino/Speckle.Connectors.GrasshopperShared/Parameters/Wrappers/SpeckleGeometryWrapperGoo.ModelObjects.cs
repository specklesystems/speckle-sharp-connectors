#if RHINO8_OR_GREATER
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Grasshopper.Rhinoceros.Render;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleGeometryWrapperGoo : GH_Goo<SpeckleGeometryWrapper>, IGH_PreviewData
{
  public SpeckleGeometryWrapperGoo(ModelObject mo)
  {
    CastFrom(mo);
  }

  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case RhinoObject rhinoObject:
        return CastFromModelObject((ModelObject)rhinoObject); // use this casting method to handle rhinoobjects: using constructor will result in a null guid!!

      case ModelObject modelObject:
        return HandleModelObject(modelObject);

      default:
        return false;
    }
  }

  private bool HandleModelObject(ModelObject modelObject)
  {
    modelObject.CastTo<IGH_GeometricGoo>(out IGH_GeometricGoo? geometryGoo);
    if (geometryGoo is null)
    {
      throw new InvalidOperationException($"Could not retrieve geometry from model object.");
    }

    GeometryBase geometryBase = geometryGoo.ToGeometryBase();
    Base? converted = SpeckleConversionContext.Current.ConvertToSpeckle(geometryBase);

    if (converted is null)
    {
      return false; // gh deals with false return from casting as warning 😎
    }

    // get layer, props, color, and mat
    SpeckleCollectionWrapper? collection = GetLayerCollectionFromModelObject(modelObject);
    SpecklePropertyGroupGoo? props = GetPropsFromModelObjectAndAssignToBase(modelObject, converted);
    SpeckleMaterialWrapper? material = GetMaterialFromModelObject(modelObject);
    Color? color = GetColorFromModelObject(modelObject, material);

    // get the definition if this is an instance
    SpeckleBlockDefinitionWrapper? definition = GetBlockDefinition(geometryBase);

    return SetValueAsObjectOrInstanceWrapper(
      geometryBase,
      converted,
      modelObject.Name.ToString(),
      props,
      collection,
      color,
      material,
      modelObject.Id?.ToString(),
      definition
    );
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

  private Rhino.Render.RenderMaterial? GetRenderMaterial(ModelObject modelObject)
  {
    // we need to retrieve the actual material by the material source (otherwise will return default material for anything other than by object)
    Guid? matId = null;
    switch (modelObject.Render?.Material?.Source)
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

  private bool SetValueAsObjectOrInstanceWrapper(
    GeometryBase geometryBase,
    Base @base,
    string name,
    SpecklePropertyGroupGoo props,
    SpeckleCollectionWrapper? parent,
    Color? color,
    SpeckleMaterialWrapper? mat,
    string? appId,
    SpeckleBlockDefinitionWrapper? definition = null
  )
  {
    string validAppId = string.IsNullOrWhiteSpace(appId) ? Guid.NewGuid().ToString() : appId!;
    Value = geometryBase is InstanceReferenceGeometry instance
      ? new SpeckleBlockInstanceWrapper()
      {
        GeometryBase = instance,
        Base = @base,
        Transform = instance.Xform,
        Definition = definition, // May be null in pure Grasshopper workflows
        Parent = parent,
        Path = parent?.Path ?? new(),
        Name = name,
        Color = color,
        Material = mat,
        Properties = props,
        ApplicationId = validAppId,
      }
      : new SpeckleGeometryWrapper()
      {
        GeometryBase = geometryBase,
        Base = @base,
        Parent = parent,
        Path = parent?.Path ?? new(),
        Name = name,
        Color = color,
        Material = mat,
        Properties = props,
        ApplicationId = validAppId,
      };

    return true;
  }

  private SpeckleBlockDefinitionWrapper? GetBlockDefinition(GeometryBase geometryBase)
  {
    SpeckleBlockDefinitionWrapper? definition = null;
    if (geometryBase is InstanceReferenceGeometry instance)
    {
      var instanceDef = RhinoDoc.ActiveDoc?.InstanceDefinitions.FindId(instance.ParentIdefId);
      if (instanceDef != null)
      {
        var defGoo = new SpeckleBlockDefinitionWrapperGoo();
        if (defGoo.CastFrom(instanceDef))
        {
          definition = defGoo.Value;
        }
      }
    }

    return definition;
  }

  private SpeckleCollectionWrapper? GetLayerCollectionFromModelObject(ModelObject modelObject)
  {
    SpeckleCollectionWrapperGoo collWrapperGoo = new();
    return collWrapperGoo.CastFrom(modelObject.Layer) ? collWrapperGoo.Value : null;
  }

  private SpecklePropertyGroupGoo GetPropsFromModelObjectAndAssignToBase(ModelObject modelObject, Base @base)
  {
    SpecklePropertyGroupGoo propertyGroup = new();
    if (propertyGroup.CastFrom(modelObject.UserText))
    {
      Dictionary<string, object?> propertyDict = new();
      foreach (var entry in modelObject.UserText)
      {
        propertyDict.Add(entry.Key, entry.Value);
      }

      @base[Constants.PROPERTIES_PROP] = propertyDict;
    }

    return propertyGroup;
  }

  private SpeckleMaterialWrapper? GetMaterialFromModelObject(ModelObject modelObject)
  {
    Rhino.Render.RenderMaterial? mat = GetRenderMaterial(modelObject);

    if (mat is Rhino.Render.RenderMaterial renderMat)
    {
      var wrapper = new SpeckleMaterialWrapperGoo();
      if (wrapper.CastFrom(renderMat))
      {
        return wrapper.Value;
      }
    }

    return null;
  }

  private Color? GetColorFromModelObject(ModelObject modelObject, SpeckleMaterialWrapper? material)
  {
    // we need to retrieve the actual color by the color source (otherwise will return default color for anything other than by object)
    int? argb = null;
    switch (modelObject.Display?.Color?.Source)
    {
      case ObjectColorSource.ColorFromLayer:
        argb = modelObject.Layer.DisplayColor?.ToArgb();
        break;
      case ObjectColorSource.ColorFromObject:
        argb = modelObject.Display.Color?.Color.ToArgb();
        break;
      case ObjectColorSource.ColorFromMaterial:
        if (material is not null)
        {
          argb = material.Material.diffuse;
        }
        break;
      default:
        break;
    }
    return argb is int validArgb ? Color.FromArgb(validArgb) : null;
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
