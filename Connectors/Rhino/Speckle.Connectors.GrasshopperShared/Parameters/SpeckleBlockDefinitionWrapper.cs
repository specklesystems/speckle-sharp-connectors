using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block definition and its converted Speckle equivalent.
/// </summary>
public class SpeckleBlockDefinitionWrapper : SpeckleWrapper
{
  public InstanceDefinitionProxy InstanceDefinitionProxy { get; set; }

  ///<remarks>
  /// `InstanceDefinitionProxy` wraps `Base` just like `SpeckleCollectionWrapper` and `SpeckleMaterialWrapper`
  /// </remarks>
  public override Base Base
  {
    get => InstanceDefinitionProxy;
    set
    {
      if (value is not InstanceDefinitionProxy instanceDefinitionProxy)
      {
        throw new ArgumentException("Cannot create block definition wrapper from a non-InstanceDefinitionProxy Base");
      }

      InstanceDefinitionProxy = instanceDefinitionProxy;
    }
  }

  /// <summary>
  /// Represents the objects contained within the block definition
  /// </summary>
  /// <remarks>
  /// Objects can contain geometry, Speckle objects, Speckle instances
  /// </remarks>
  public List<SpeckleObjectWrapper> Objects { get; set; } = new(); // TODO: This isn't handling instances!

  // TODO: we need to wait on this. not sure how to tackle this 🤯 overrides etc.
  /*public Color? Color { get; set; }
  public SpeckleMaterialWrapper? Material { get; set; }*/

  public override string ToString() => $"Speckle Block Definition [{Name}]";

  /// <summary>
  /// Creates a preview of the block definition by displaying all contained objects
  /// </summary>
  /// <remarks>
  /// Leveraging already defined preview logic for the objects which make up this block. Refer to <see cref="SpeckleObjectWrapper.DrawPreview"/>.
  /// </remarks>
  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
  {
    foreach (var obj in Objects)
    {
      obj.DrawPreview(args, isSelected);
    }
  }

  public void DrawPreviewRaw(DisplayPipeline display, DisplayMaterial material) =>
    throw new NotImplementedException(
      "Block definitions can't use a single material for all objects. Rather use DrawPreview() which respects individual material definitions."
    );

  public (int index, bool existingDefinitionUpdated) Bake(RhinoDoc doc, List<Guid> obj_ids, string? name = null)
  {
    string blockDefinitionName = name ?? Name;

    // NOTE: below is pretty much a copy of `SpeckleObjectWrapper`s `Bake` method
    // TODO: reduce code duplicity between this `Bake` method and others
    var geometries = new List<GeometryBase>();
    var attributes = new List<ObjectAttributes>();

    foreach (var obj in Objects)
    {
      if (obj.GeometryBase != null)
      {
        geometries.Add(obj.GeometryBase);

        var att = new ObjectAttributes { Name = obj.Name };

        if (obj.Color is Color c)
        {
          att.ObjectColor = c;
          att.ColorSource = ObjectColorSource.ColorFromObject;
        }

        if (obj.Material is SpeckleMaterialWrapper m)
        {
          int matIndex = m.Bake(doc, m.Name); // bake material to get index
          if (matIndex != -1)
          {
            att.MaterialIndex = matIndex;
            att.MaterialSource = ObjectMaterialSource.MaterialFromObject;
          }
        }

        foreach (var kvp in obj.Properties.Value)
        {
          att.SetUserString(kvp.Key, kvp.Value?.ToString() ?? string.Empty);
        }

        attributes.Add(att);
      }
    }

    if (geometries.Count == 0)
    {
      return (-1, false); // no geometry to create a block definition. this is essentially a fail
    }

    // Check if definition already exists
    var existingDef = doc.InstanceDefinitions.Find(blockDefinitionName);

    int blockDefinitionIndex;
    Guid blockDefinitionId;

    if (existingDef != null)
    {
      // update existing definition
      bool success = doc.InstanceDefinitions.ModifyGeometry(
        existingDef.Index,
        geometries.ToArray(),
        attributes.ToArray()
      );

      if (!success)
      {
        return (-1, true); // tried to update but failed
      }

      blockDefinitionIndex = existingDef.Index;
      blockDefinitionId = existingDef.Id;
    }
    else
    {
      // Create new definition
      blockDefinitionIndex = doc.InstanceDefinitions.Add(
        blockDefinitionName,
        string.Empty, // NOTE: currently no description
        Point3d.Origin, // NOTE: will be baked with ref point at origin
        geometries,
        attributes
      );

      if (blockDefinitionIndex == -1) // returns -1 on failure and a valid index (≥ 0) on success
      {
        return (-1, false); // creation failed
      }

      blockDefinitionId = doc.InstanceDefinitions[blockDefinitionIndex].Id;
    }

    obj_ids.Add(blockDefinitionId);
    return (blockDefinitionIndex, existingDef != null);
  }

  public SpeckleBlockDefinitionWrapper DeepCopy() =>
    new()
    {
      Base = InstanceDefinitionProxy.ShallowCopy(),
      /*Color = Color,
      Material = Material,*/
      ApplicationId = ApplicationId,
      Name = Name,
      Objects = Objects.Select(o => o.DeepCopy()).ToList()
    };
}

public partial class SpeckleBlockDefinitionWrapperGoo
  : GH_Goo<SpeckleBlockDefinitionWrapper>,
    IGH_PreviewData,
    ISpeckleGoo
{
  public override IGH_Goo Duplicate() => new SpeckleBlockDefinitionWrapperGoo(Value.DeepCopy());

  public override string ToString() => $@"Speckle Block Definition Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => Value?.InstanceDefinitionProxy is not null;
  public override string TypeName => "Speckle block definition wrapper";
  public override string TypeDescription => "A wrapper around speckle instance definition proxies.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockDefinitionWrapper wrapper:
        Value = wrapper.DeepCopy();
        return true;

      case GH_Goo<SpeckleBlockDefinitionWrapper> blockDefinitionGoo:
        Value = blockDefinitionGoo.Value.DeepCopy();
        return true;

      case InstanceDefinitionProxy speckleInstanceDefProxy:
        Value = new SpeckleBlockDefinitionWrapper()
        {
          Base = speckleInstanceDefProxy,
          Name = speckleInstanceDefProxy.name,
          ApplicationId = speckleInstanceDefProxy.applicationId ?? Guid.NewGuid().ToString()
        };
        return true;

      case InstanceDefinition rhinoInstanceDef:
        return CastFromRhinoInstanceDefinition(rhinoInstanceDef);
    }

    // Handle Rhino 8 Model Objects
    return CastFromModelObject(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);

    if (type == typeof(InstanceDefinitionProxy))
    {
      target = (T)(object)Value.InstanceDefinitionProxy;
      return true;
    }

    if (type == typeof(InstanceDefinition))
    {
      return CastToRhinoInstanceDefinition(ref target);
    }

    return CastToModelObject(ref target);
  }

  /// <summary>
  /// Converts a Rhino <see cref="InstanceDefinition"/> to a <see cref="SpeckleBlockDefinitionWrapper"/>.
  /// Takes all objects from the instance definition and converts them to <see cref="SpeckleObjectWrapper"/>s.
  /// </summary>
  private bool CastFromRhinoInstanceDefinition(InstanceDefinition instanceDef)
  {
    try
    {
      var objects = new List<SpeckleObjectWrapper>();
      var objectIds = new List<string>();

      var rhinoObjects = instanceDef.GetObjects(); // Get the objects that DEFINE the block, not all instances of it
      foreach (var rhinoObj in rhinoObjects)
      {
        if (rhinoObj?.Geometry != null)
        {
          Base converted; // SpeckleConversionContext.ConvertToSpeckle() is for basic geometry, we may have nested instances

          if (rhinoObj.Geometry is InstanceReferenceGeometry instanceRef) // nested!
          {
            // get nested instance definition for name lookup
            var nestedInstanceDef = RhinoDoc.ActiveDoc?.InstanceDefinitions.FindId(instanceRef.ParentIdefId);
            string definitionName = nestedInstanceDef?.Name ?? instanceRef.ParentIdefId.ToString();

            // create an InstanceProxy for nested blocks using correct properties
            var instanceProxy = new InstanceProxy
            {
              definitionId = instanceRef.ParentIdefId.ToString(),
              transform = ConvertRhinoTransformToMatrix4x4(instanceRef.Xform),
              units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none",
              maxDepth = 1,
              applicationId = rhinoObj.Id.ToString()
            };
            instanceProxy["definitionName"] = definitionName;
            converted = instanceProxy;
          }
          else // regular geometry
          {
            converted = SpeckleConversionContext.ConvertToSpeckle(rhinoObj.Geometry);
          }

          converted[Constants.NAME_PROP] = rhinoObj.Name ?? "";
          converted.applicationId = rhinoObj.Id.ToString();

          var objWrapper = new SpeckleObjectWrapper()
          {
            Base = converted,
            GeometryBase = rhinoObj.Geometry,
            Properties = new SpecklePropertyGroupGoo(),
            Name = rhinoObj.Name ?? "",
            /*Color = GetObjectColor(rhinoObj),
            Material = GetObjectMaterial(rhinoObj),*/
            WrapperGuid = rhinoObj.Id.ToString(),
            ApplicationId = rhinoObj.Id.ToString(),
            Path = new List<string>(),
            Parent = null
          };

          if (rhinoObj.Attributes?.UserStringCount > 0)
          {
            var userStrings = new Dictionary<string, object?>();
            foreach (string key in rhinoObj.Attributes.GetUserStrings())
            {
              userStrings[key] = rhinoObj.Attributes.GetUserString(key);
            }
            objWrapper.Properties.CastFrom(userStrings);
          }

          objects.Add(objWrapper);
          objectIds.Add(converted.applicationId);
        }
      }

      var speckleInstanceDefProxy = new InstanceDefinitionProxy
      {
        name = instanceDef.Name,
        applicationId = instanceDef.Id.ToString(),
        objects = objectIds,
        maxDepth = 1 // default depth for single-level block definition (I think?)
      };

      Value = new SpeckleBlockDefinitionWrapper()
      {
        Base = speckleInstanceDefProxy,
        Name = instanceDef.Name,
        Objects = objects,
        ApplicationId = instanceDef.Id.ToString()
      };

      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return false;
    }
  }

  /// <summary>
  /// Attempts to cast to a Rhino <see cref="InstanceDefinition"/> by finding it in the active document.
  /// </summary>
  /// <remarks>
  /// Cannot create new instance definitions through casting - use <see cref="SpeckleBlockDefinitionWrapper.Bake"/> instead.
  /// </remarks>
  private bool CastToRhinoInstanceDefinition<T>(ref T target)
  {
    var type = typeof(T);
    if (type != typeof(InstanceDefinition))
    {
      return false;
    }

    try
    {
      var doc = RhinoDoc.ActiveDoc; // TODO: is this the right way to access doc?
      var instanceDefinition = doc?.InstanceDefinitions.Find(Value.Name);
      if (instanceDefinition != null)
      {
        target = (T)(object)instanceDefinition;
        return true;
      }

      // if not found in doc, we cannot create an InstanceDefinition through casting - user should call Bake() method instead
      return false;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return false;
    }
  }

  public void DrawViewportWires(GH_PreviewWireArgs args)
  {
    // TODO: Do block definitions even have separate wire preview?
  }

  /// <summary>
  /// Draws viewport meshes by iterating through all objects in the block definition.
  /// Each object renders using its OWN material properties.
  /// </summary>
  public void DrawViewportMeshes(GH_PreviewMeshArgs args)
  {
    if (Value?.Objects == null)
    {
      return;
    }

    foreach (var obj in Value.Objects)
    {
      if (obj.GeometryBase != null)
      {
        obj.DrawPreviewRaw(args.Pipeline, args.Material);
      }
    }
  }

  public BoundingBox ClippingBox
  {
    get
    {
      BoundingBox clippingBox = new();
      if (Value?.Objects != null)
      {
        foreach (var obj in Value.Objects)
        {
          if (obj.GeometryBase != null)
          {
            clippingBox.Union(obj.GeometryBase.GetBoundingBox(false));
          }
        }
      }
      return clippingBox;
    }
  }

  /// <summary>
  /// Creates a deep copy of this block definition wrapper for proper data handling.
  /// Follows the same pattern as other Goo implementations in the codebase.
  /// </summary>
  /// <returns>A new instance with copied data</returns>
  public SpeckleBlockDefinitionWrapper DeepCopy() =>
    new()
    {
      Base = Value.InstanceDefinitionProxy.ShallowCopy(),
      Name = Value.Name,
      Objects = Value.Objects.Select(o => o.DeepCopy()).ToList(),
      ApplicationId = Value.ApplicationId
    };

  public SpeckleBlockDefinitionWrapperGoo(SpeckleBlockDefinitionWrapper value)
  {
    Value = value;
  }

  public SpeckleBlockDefinitionWrapperGoo()
  {
    Value = new()
    {
      Base = new InstanceDefinitionProxy
      {
        name = "Unnamed Block",
        objects = new List<string>(),
        maxDepth = 1
      },
    };
  }

  /// <summary>
  /// Converts a Rhino Transform to a Speckle Matrix4x4
  /// </summary>
  private Matrix4x4 ConvertRhinoTransformToMatrix4x4(Transform rhinoTransform) =>
    new(
      rhinoTransform.M00,
      rhinoTransform.M01,
      rhinoTransform.M02,
      rhinoTransform.M03,
      rhinoTransform.M10,
      rhinoTransform.M11,
      rhinoTransform.M12,
      rhinoTransform.M13,
      rhinoTransform.M20,
      rhinoTransform.M21,
      rhinoTransform.M22,
      rhinoTransform.M23,
      rhinoTransform.M30,
      rhinoTransform.M31,
      rhinoTransform.M32,
      rhinoTransform.M33
    );
}

public class SpeckleBlockDefinitionWrapperParam
  : GH_Param<SpeckleBlockDefinitionWrapperGoo>,
    IGH_BakeAwareObject,
    IGH_PreviewObject
{
  public SpeckleBlockDefinitionWrapperParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleBlockDefinitionWrapperParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleBlockDefinitionWrapperParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleBlockDefinitionWrapperParam(GH_ParamAccess access)
    : base(
      "Speckle Block Definition",
      "SBD",
      "Returns a Speckle Block definition.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("C71BE6AD-E27B-4E7F-87DA-569D4DEE77BE");

  protected override Bitmap Icon => Resources.speckle_param_object; // TODO: claire Icon for speckle param block instance

  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => BakeAllItems(doc, obj_ids);

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids) =>
    // we "ignore" the ObjectAttributes parameter because definitions manage their own internal object attributes
    // atts aren't on a definition level, but either on an instance level and/or objects within a definition (right?)
    BakeAllItems(doc, obj_ids);

  private void BakeAllItems(RhinoDoc doc, List<Guid> obj_ids)
  {
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockDefinitionWrapperGoo goo)
      {
        var (index, wasUpdated) = goo.Value.Bake(doc, obj_ids);

        if (index != -1)
        {
          string message = wasUpdated // little UX sparkle. baking and updating existing definitions is dangerous. this way, we let them know.
            ? $"Updated existing block definition: '{goo.Value.Name}'"
            : $"Created new block definition: '{goo.Value.Name}'";
          AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);
        }
        else
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to bake block definition: '{goo.Value.Name}'");
        }
      }
    }
  }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // TODO: Do block definitions even have separate wire preview?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this);
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleBlockDefinitionWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }

  public bool Hidden { get; set; }
  public BoundingBox ClippingBox
  {
    get
    {
      BoundingBox clippingBox = new();
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleBlockDefinitionWrapperGoo goo && goo.Value?.Objects != null)
        {
          foreach (var obj in goo.Value.Objects)
          {
            if (obj.GeometryBase != null)
            {
              clippingBox.Union(obj.GeometryBase.GetBoundingBox(false));
            }
          }
        }
      }
      return clippingBox;
    }
  }
}
