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
      case InstanceDefinition instanceDefinition:
        List<SpeckleObjectWrapper> objects = new();
        foreach (var defObj in instanceDefinition.GetObjects())
        {
          ModelObject defModelObj = new(); // TODO!! MODEL OBJECTS ARE DUMB AND DON'T RESULT IN A VALID ID WHEN CONSTRUCTED THIS WAY
          SpeckleObjectWrapperGoo defObjGoo = new();
          if (defObjGoo.CastFrom(defObj))
          {
            objects.Add(defObjGoo.Value);
          }
        }

        if (objects.Count == 0)
        {
          return false;
        }

        Value = new SpeckleBlockDefinitionWrapper()
        {
          Base = new InstanceDefinitionProxy
          {
            name = instanceDefinition.Name,
            objects = objects.Select(o => o.ApplicationId!).ToList(),
            maxDepth = 0 // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
          },
          Name = instanceDefinition.Name,
          Objects = objects,
          ApplicationId = instanceDefinition.Id.ToString()
        };

        return true;

      case ModelInstanceDefinition modelInstanceDef:
        InstanceDefinition? instanceDef = RhinoDoc.ActiveDoc?.InstanceDefinitions.Find(modelInstanceDef.Name);
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

        return CastFromModelObject(instanceDef);
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
      var instanceDef = doc?.InstanceDefinitions.Find(Value.Name); // POC: this seems dangerous as users can change rhino block names
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
}
#endif
