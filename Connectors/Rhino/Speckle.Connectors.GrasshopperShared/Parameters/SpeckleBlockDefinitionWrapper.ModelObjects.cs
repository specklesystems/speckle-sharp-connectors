#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros.Model;
using Rhino;
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
    var instanceDef = doc?.InstanceDefinitions.Find(modelInstanceDef.Name);
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
      var objWrapperGoo = new SpeckleObjectWrapperGoo();
      if (objWrapperGoo.CastFrom(modelObj))
      {
        objects.Add(objWrapperGoo.Value);
      }
    }

    Value = new SpeckleBlockDefinitionWrapper()
    {
      Base = new InstanceDefinitionProxy
      {
        name = modelInstanceDef.Name,
        applicationId = modelInstanceDef.Id?.ToString() ?? Guid.NewGuid().ToString(),
        objects = objects.Select(o => o.ApplicationId ?? Guid.NewGuid().ToString()).ToList(),
        maxDepth = 1
      },
      Name = modelInstanceDef.Name,
      ApplicationId = modelInstanceDef.Id?.ToString() ?? Guid.NewGuid().ToString(),
      Objects = objects
    };

    return objects.Count > 0;
  }
}
#endif
