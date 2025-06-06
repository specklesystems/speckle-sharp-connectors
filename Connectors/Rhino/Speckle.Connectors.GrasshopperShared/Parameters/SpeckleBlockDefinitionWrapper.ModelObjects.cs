#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros.Model;
using Rhino;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockDefinitionWrapperGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case ModelInstanceDefinition modelInstanceDef:
        // ModelInstanceDefinition (Rhino 8) wraps InstanceDefinition (Rhino 7 and 8), so we're "downgrading" here
        // But this way, both can use common CastFromRhinoInstanceDefinition method
        var rhinoInstanceDef = RhinoDoc.ActiveDoc?.InstanceDefinitions.Find(modelInstanceDef.Name);
        if (rhinoInstanceDef != null)
        {
          return CastFromRhinoInstanceDefinition(rhinoInstanceDef);
        }
        return false;

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
}
#endif
