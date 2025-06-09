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
        if (modelInstanceDef.Id == null)
        {
          // this is a definition with Grasshopper-only objects that we can't process
          // .Objects returns ModelObjects which rely on Rhino Doc for casting, so we're pretty stuck at this point 😓
          throw new InvalidOperationException(
            $"Cannot convert native Grasshopper block definitions. Please bake to Rhino document first or use Speckle Block Definition components."
          );
        }
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
