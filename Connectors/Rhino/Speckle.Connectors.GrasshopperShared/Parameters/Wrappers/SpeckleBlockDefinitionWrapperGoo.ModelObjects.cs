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
        return TryConvertDefinition(
          instanceDefinition.GetObjects().Cast<object>(),
          instanceDefinition.Name,
          instanceDefinition.Id.ToString()
        );

      case ModelInstanceDefinition modelInstanceDef:
        return TryConvertDefinition(
          modelInstanceDef.Objects.Cast<object>(),
          modelInstanceDef.Name,
          modelInstanceDef.Id.ToString()
        );

      default:
        return false;
    }
  }

  /// <summary>
  /// Attempts to convert block definition objects to SpeckleGeometryWrappers.
  /// Returns false if all objects are unsupported, true if at least one converts.
  /// </summary>
  private bool TryConvertDefinition(IEnumerable<object> definitionObjects, string definitionName, string definitionId)
  {
    var objects = definitionObjects as object[] ?? definitionObjects.ToArray();
    int totalCount = objects.Length;
    List<SpeckleGeometryWrapper> converted = new();

    foreach (var defObj in objects)
    {
      SpeckleGeometryWrapperGoo defObjGoo = new();
      if (defObjGoo.CastFrom(defObj))
      {
        converted.Add(defObjGoo.Value);
      }
    }

    int skippedCount = totalCount - converted.Count;

    // return false if nothing converted - Grasshopper handles this as warning (CNX-2855)
    if (converted.Count == 0)
    {
      return false;
    }

    // show debug info if some objects skipped (CNX-2855)
    if (skippedCount > 0)
    {
      System.Diagnostics.Debug.WriteLine($"Block '{definitionName}' skipped {skippedCount} unsupported object(s)");
    }

    SetValueFromDefinitionProps(converted, definitionName, definitionId);
    return true;
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
