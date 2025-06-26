#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.DocObjects;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockDefinitionWrapperGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case ModelInstanceDefinition modelInstanceDef:
        return CastFromModelInstanceDefinition(modelInstanceDef);
      default:
        return false;
    }
  }

  private bool CastToModelObject<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(ModelInstanceDefinition))
    {
      var doc = RhinoDoc.ActiveDoc;
      var instanceDef = doc?.InstanceDefinitions.Find(Value.Name);
      if (instanceDef != null)
      {
        // ⚠️ ModelInstanceDefinition(InstanceDefinition) constructor strips .Id and we can't set it afterward
        var modelInstanceDef = new ModelInstanceDefinition(instanceDef);
        target = (T)(object)modelInstanceDef;
        return true;
      }

      return false;
    }

    return false;
  }

  private bool CastFromModelInstanceDefinition(ModelInstanceDefinition modelInstanceDef)
  {
    var doc = RhinoDoc.ActiveDoc;
    InstanceDefinition? instanceDef = doc?.InstanceDefinitions.Find(modelInstanceDef.Name);
    if (instanceDef == null)
    {
      // Rhino → Model → Model Block Definition passthrough component returns type ModelInstanceDefinition
      // .Objects of a ModelInstanceDefinition returns ModelObjects
      // ModelObject.Geometry is internal and cannot be accessed directly.
      // Only way to get geometry from a ModelObject is through RhinoDoc.Objects.FindId(), which only works for baked objects.
      // Unbaked Grasshopper geometry cannot be processed through the ModelObject workflow until we get a public geometry accessor 😓
      // ⚠️ So if user defines a Model Block Definition in Grasshopper with Grasshopper (unbaked) geometry, we're stuck.
      // That's why we're intercepting this case early → if the instanceDef == null don't go further
      throw new InvalidOperationException(
        $"Block definition '{modelInstanceDef.Name}' not found in Rhino document. Please bake the definition first or use Speckle Block Definition components instead."
      );
    }

    var objects = new List<SpeckleObjectWrapper>();
    var modelObjects = modelInstanceDef.Objects ?? Array.Empty<ModelObject>();

    foreach (var modelObj in modelObjects)
    {
      var wrapperGoo = ConvertModelObjectToAppropriateWrapper(modelObj);
      if (wrapperGoo != null)
      {
        objects.Add(wrapperGoo);
      }
    }

    Value = new SpeckleBlockDefinitionWrapper()
    {
      Base = new InstanceDefinitionProxy
      {
        name = modelInstanceDef.Name,
        objects = objects.Select(o => o.ApplicationId!).ToList(),
        maxDepth = 0 // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
      },
      Name = modelInstanceDef.Name,
      Objects = objects,
      ApplicationId = instanceDef.Id.ToString()
    };

    return objects.Count > 0;
  }

  private SpeckleObjectWrapper? ConvertModelObjectToAppropriateWrapper(ModelObject modelObj)
  {
    // Handle the special case: Instance references need block instance wrapper
    if (modelObj.ObjectType == ObjectType.InstanceReference)
    {
      var blockInstGoo = new SpeckleBlockInstanceWrapperGoo();
      return blockInstGoo.CastFrom(modelObj) ? blockInstGoo.Value : null;
    }

    // Everything else goes to regular object wrapper
    var objWrapperGoo = new SpeckleObjectWrapperGoo();
    return objWrapperGoo.CastFrom(modelObj) ? objWrapperGoo.Value : null;
  }
}
#endif
