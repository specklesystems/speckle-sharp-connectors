#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockInstanceWrapperGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case InstanceReferenceGeometry instanceRef:
        return CreateFromInstanceReference(instanceRef);

      case GH_InstanceReference ghInstanceRef:
        return ghInstanceRef.Value != null && CreateFromInstanceReference(ghInstanceRef.Value);

      case ModelInstanceDefinition modelInstanceDef:
        return CastFromModelInstanceDefinition(modelInstanceDef);

      default:
        return false;
    }
  }

  private bool CastToModelObject<T>(ref T target)
  {
    switch (target)
    {
      case GH_InstanceReference:
        if (Value == null)
        {
          return false;
        }

        if (Value.Definition == null)
        {
          var minimalInstanceRef = new InstanceReferenceGeometry(Guid.Empty, Value.Transform);
          target = (T)(object)new GH_InstanceReference(minimalInstanceRef);
          return true;
        }

        var modelInstanceDef = CreateModelInstanceDefinition(Value.Definition);
        if (modelInstanceDef == null)
        {
          return false;
        }

        var instanceRefGeo = new InstanceReferenceGeometry(Guid.Empty, Value.Transform);
        var ghInstanceRef = new GH_InstanceReference(instanceRefGeo, modelInstanceDef);
        target = (T)(object)ghInstanceRef;
        return true;

      case InstanceReferenceGeometry:
        return CreateInstanceReferenceGeometry(ref target);

      default:
        return false;
    }
  }

  private bool CastFromModelInstanceDefinition(ModelInstanceDefinition modelInstanceDef)
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";

    Value = new SpeckleBlockInstanceWrapper()
    {
      Base = new InstanceProxy()
      {
        definitionId = modelInstanceDef.Id?.ToString() ?? "unknown",
        maxDepth = 1,
        transform = GrasshopperHelpers.TransformToMatrix(Transform.Identity, units),
        units = units,
        applicationId = Guid.NewGuid().ToString()
      },
      Transform = Transform.Identity,
      ApplicationId = Guid.NewGuid().ToString()
    };
    return true;
  }

  private ModelInstanceDefinition? CreateModelInstanceDefinition(SpeckleBlockDefinitionWrapper definition)
  {
    var modelInstanceDefGoo = new SpeckleBlockDefinitionWrapperGoo(definition);
    ModelInstanceDefinition existingModelDef = new();
    if (modelInstanceDefGoo.CastTo(ref existingModelDef))
    {
      return existingModelDef;
    }

    var doc = RhinoDoc.ActiveDoc;

    if (doc == null)
    {
      return null;
    }

    var rhinoInstanceDef = doc.InstanceDefinitions.Find(definition.Name);

    if (rhinoInstanceDef != null)
    {
      return new ModelInstanceDefinition(rhinoInstanceDef);
    }

    var geometries = new List<GeometryBase>();
    var attributes = new List<ObjectAttributes>();

    foreach (var obj in definition.Objects)
    {
      if (obj.GeometryBase != null)
      {
        geometries.Add(obj.GeometryBase.Duplicate());

        var att = new ObjectAttributes { Name = obj.Name };
        if (obj.Color is Color color)
        {
          att.ObjectColor = color;
          att.ColorSource = ObjectColorSource.ColorFromObject;
        }

        foreach (var kvp in obj.Properties.Value)
        {
          att.SetUserString(kvp.Key, kvp.Value?.ToString() ?? "");
        }

        attributes.Add(att);
      }
    }

    if (geometries.Count == 0)
    {
      return null;
    }

    var defIndex = doc.InstanceDefinitions.Add(
      definition.Name,
      "Temporary for Grasshopper workflow - objects will appear as point on bake",
      Point3d.Origin,
      geometries,
      attributes
    );

    if (defIndex == -1)
    {
      return null;
    }

    var tempRhinoDef = doc.InstanceDefinitions[defIndex];
    var modelDef = new ModelInstanceDefinition(tempRhinoDef);

    return modelDef;
  }

  private bool CreateFromInstanceReference(InstanceReferenceGeometry instanceRef)
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
    var definitionId = instanceRef.ParentIdefId;

    // Try to preserve existing definition first (for round-trip scenarios)
    SpeckleBlockDefinitionWrapper? definition = Value?.Definition;

    // Look in document if we don't have an existing definition
    if (definition == null)
    {
      var doc = RhinoDoc.ActiveDoc;
      var instanceDef = doc?.InstanceDefinitions.FindId(definitionId);
      if (instanceDef != null)
      {
        var defGoo = new SpeckleBlockDefinitionWrapperGoo();
        if (defGoo.CastFrom(instanceDef))
        {
          definition = defGoo.Value;
        }
      }
    }

    Value = new SpeckleBlockInstanceWrapper()
    {
      InstanceProxy = new InstanceProxy()
      {
        definitionId = definitionId.ToString(),
        maxDepth = 1,
        transform = GrasshopperHelpers.TransformToMatrix(instanceRef.Xform, units),
        units = units,
        applicationId = Guid.NewGuid().ToString()
      },
      Transform = instanceRef.Xform,
      ApplicationId = Guid.NewGuid().ToString(),
      Definition = definition // May be null in pure Grasshopper workflows
    };
    return true;
  }

  private bool CreateInstanceReferenceGeometry<T>(ref T target)
  {
    // Only works if the block definition exists in the Rhino document
    // Will fail for pure Grasshopper workflows
    if (Value?.Definition == null)
    {
      return false;
    }

    var doc = RhinoDoc.ActiveDoc;
    var instanceDef = doc?.InstanceDefinitions.Find(Value.Definition.Name);

    if (instanceDef != null)
    {
      var instanceRefGeo = new InstanceReferenceGeometry(instanceDef.Id, Value.Transform);
      target = (T)(object)instanceRefGeo;
      return true;
    }

    return false;
  }
}
#endif
