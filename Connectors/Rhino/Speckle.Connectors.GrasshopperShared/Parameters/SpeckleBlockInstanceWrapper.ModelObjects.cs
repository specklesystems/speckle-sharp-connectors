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
    var type = typeof(T);

    // User connects our output to Grasshopper's native block instance parameter
    if (type == typeof(GH_InstanceReference))
    {
      if (Value == null)
      {
        return false;
      }

      if (Value.Definition == null)
      {
        // For pure Grasshopper workflows, we don't have a Model Definition in doc which we can reference
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
    }

    return false;
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
    try
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
            att.SetUserString(kvp.Key, kvp.Value.Value?.ToString() ?? "");
          }

          attributes.Add(att);
        }
      }

      if (geometries.Count == 0)
      {
        Console.WriteLine("geometries are empty, just like my stomach. fuck.");
        return null;
      }

      var defIndex = doc.InstanceDefinitions.Add(
        definition.Name,
        "Temporary for Grasshopper workflow",
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
#pragma warning disable CA1031
    catch (Exception ex)
#pragma warning restore CA1031
    {
      System.Diagnostics.Debug.WriteLine($"Error creating ModelInstanceDefinition: {ex.Message}");
      return null;
    }
  }
}
#endif
