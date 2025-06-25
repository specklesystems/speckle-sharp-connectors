#if RHINO8_OR_GREATER
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Common;
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

      // When implementing nested blocks support, discovered that nested blocks coming from Rhino arrive as ModelObjects
      // containing InstanceReferenceGeometry.
      case ModelObject modelObject:
        return CreateFromModelObject(modelObject);

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

  private bool CreateFromInstanceReference(InstanceReferenceGeometry instanceRef, string? appId = null)
  {
    var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? Units.None;
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
      Base = new InstanceProxy()
      {
        definitionId = definitionId.ToString(),
        maxDepth = 0, // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
        transform = GrasshopperHelpers.TransformToMatrix(instanceRef.Xform, units),
        units = units
      },
      ApplicationId = appId,
      Transform = instanceRef.Xform,
      Definition = definition, // May be null in pure Grasshopper workflows
      GeometryBase = new InstanceReferenceGeometry(definitionId, instanceRef.Xform)
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

  /// <summary>
  /// Nested blocks from Rhino come wrapped in ModelObject containers. ModelObject contains InstanceReferenceGeometry +
  /// metadata (ID, layer, materials, etc.). We need to extract the InstanceReferenceGeometry from the ModelObject
  /// and process it with existing logic.
  /// </summary>
  private bool CreateFromModelObject(ModelObject modelObject)
  {
    // GUARD: Only handle InstanceReference ModelObjects
    // (SpeckleObjectWrapper handles all other geometry types)
    if (modelObject.ObjectType != ObjectType.InstanceReference)
    {
      return false;
    }

    // EXTRACT: Get the InstanceReferenceGeometry from ModelObject container
    // Same pattern as SpeckleObjectWrapper: ModelObject → GeometryBase extraction
    // Inline helper to keep geometry extraction logic contained within this method
    GeometryBase? geometryBase = RhinoDoc.ActiveDoc.Objects.FindId(modelObject.Id ?? Guid.Empty)?.Geometry;
    if (geometryBase is not InstanceReferenceGeometry instanceRefGeo)
    {
      return false;
    }

    return CreateFromInstanceReference(instanceRefGeo, modelObject.Id?.ToString());
  }
}
#endif
