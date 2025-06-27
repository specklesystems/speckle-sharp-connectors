#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

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

      // Rhino model objects can be instances
      case ModelObject modelObject:
        if (modelObject.ObjectType == ObjectType.InstanceReference)
        {
          SpeckleObjectWrapperGoo objGoo = new();
          objGoo.CastFrom(modelObject); // handles all model object casting like geo conversion, model object name and props and color and mat

          if (objGoo.Value is SpeckleBlockInstanceWrapper instanceWrapper)
          {
            Value = instanceWrapper;
            return true;
          }
        }

        return false;

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
          // No definition available - create minimal instance reference for compatibility
          // This handles edge cases where we have a transform but no block definition
          var minimalInstanceRef = new InstanceReferenceGeometry(Guid.Empty, Value.Transform);
          target = (T)(object)new GH_InstanceReference(minimalInstanceRef);
          return true;
        }

        // Create or find the block definition in the Rhino document
        // This either finds existing definition or creates temporary one for pure GH workflows
        var modelInstanceDef = CreateModelInstanceDefinition(Value.Definition);
        if (modelInstanceDef == null)
        {
          return false;
        }

        // ModelInstanceDefinition.Id contains the real definition ID from document (either found or just created)
        // Fallback to Guid.Empty only for theoretical edge cases where ID might be null
        var definitionId = modelInstanceDef.Id ?? Guid.Empty;

        // Create InstanceReferenceGeometry with the actual definition ID
        // This preserves the link to the real block definition in the Rhino document (if any)
        var instanceRefGeo = new InstanceReferenceGeometry(definitionId, Value.Transform);
        var ghInstanceRef = new GH_InstanceReference(instanceRefGeo, modelInstanceDef);
        target = (T)(object)ghInstanceRef;
        return true;

      case InstanceReferenceGeometry:
        return CreateInstanceReferenceGeometry(ref target);

      default:
        return false;
    }
  }

  private ModelInstanceDefinition? CreateModelInstanceDefinition(SpeckleBlockDefinitionWrapper definition)
  {
    SpeckleBlockDefinitionWrapperGoo modelInstanceDefGoo = new(definition);
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
        ObjectAttributes att = obj.CreateObjectAttributes();
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

    InstanceDefinition? tempRhinoDef = doc.InstanceDefinitions[defIndex];
    ModelInstanceDefinition modelDef = new(tempRhinoDef);

    return modelDef;
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
