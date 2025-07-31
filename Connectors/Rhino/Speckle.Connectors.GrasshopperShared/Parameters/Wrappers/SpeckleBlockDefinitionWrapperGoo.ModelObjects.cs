#if RHINO8_OR_GREATER
using Grasshopper.Rhinoceros.Model;
using Rhino.DocObjects;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockDefinitionWrapperGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case InstanceDefinition instanceDefinition:
        List<SpeckleGeometryWrapper> objects = new();
        foreach (var defObj in instanceDefinition.GetObjects())
        {
          SpeckleGeometryWrapperGoo defObjGoo = new();
          if (defObjGoo.CastFrom(defObj))
          {
            objects.Add(defObjGoo.Value);
          }
        }

        if (objects.Count == 0)
        {
          return false;
        }

        SetValueFromDefinitionProps(objects, instanceDefinition.Name, instanceDefinition.Id.ToString());
        return true;

      case ModelInstanceDefinition modelInstanceDef:
        List<SpeckleGeometryWrapper> defObjs = new();
        foreach (var defObj in modelInstanceDef.Objects)
        {
          SpeckleGeometryWrapperGoo geoWrapperGoo = new();
          if (geoWrapperGoo.CastFrom(defObj))
          {
            defObjs.Add(geoWrapperGoo.Value);
          }
        }

        if (defObjs.Count == 0)
        {
          throw new InvalidOperationException(
            $"Block definition '{modelInstanceDef.Name}' did not have any valid geometry."
          );
        }

        SetValueFromDefinitionProps(defObjs, modelInstanceDef.Name, modelInstanceDef.Id.ToString());
        return true;
      default:
        return false;
    }
  }

  private void SetValueFromDefinitionProps(List<SpeckleGeometryWrapper> objs, string name, string id)
  {
    string validAppId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString() : id;
    Value = new SpeckleBlockDefinitionWrapper()
    {
      Base = new InstanceDefinitionProxy
      {
        name = name,
        objects = objs.Select(o => o.ApplicationId!).ToList(),
        maxDepth = 0 // represent newly created, top-level objects. actual depth calculation happens in GrasshopperBlockPacker
      },
      Name = name,
      Objects = objs,
      ApplicationId = validAppId
    };
  }

  private bool CastToModelObject<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(ModelInstanceDefinition))
    {
      var doc = CurrentDocument.Document;
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
