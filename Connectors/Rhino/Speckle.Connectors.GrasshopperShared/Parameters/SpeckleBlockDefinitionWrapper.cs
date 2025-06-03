using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
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

  public int Bake(RhinoDoc doc, List<Guid> obj_ids, string? name = null)
  {
    string blockDefinitionName = name ?? Name;

    if (doc.InstanceDefinitions.Find(blockDefinitionName) is not null)
    {
      throw new SpeckleException(
        $"Cannot create block definition '{blockDefinitionName}' since a definition with this name already exists in the doc."
      ); // alternatively, we append something to the blockDefinitionName to make it unique? I'd hate to manipulate existing block
    }

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
      return -1; // no geometry to create a block definition. this is essentially a fail
    }

    int blockDefinitionIndex = doc.InstanceDefinitions.Add(
      blockDefinitionName,
      string.Empty, // NOTE: currently no description
      Point3d.Origin, // NOTE: will be baked with ref point at origin
      geometries,
      attributes
    );

    if (blockDefinitionIndex != -1) // returns -1 on failure and a valid index (≥ 0) on success
    {
      obj_ids.Add(doc.InstanceDefinitions[blockDefinitionIndex].Id);
    }

    return blockDefinitionIndex;
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

      default:
        return false;
    }
  }

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

    return false;
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

      var rhinoObjects = instanceDef.GetReferences(0); // get all objects in the instance definition
      foreach (var rhinoObj in rhinoObjects)
      {
        if (rhinoObj?.Geometry != null)
        {
          var converted = SpeckleConversionContext.ConvertToSpeckle(rhinoObj.Geometry);
          converted[Constants.NAME_PROP] = rhinoObj.Name ?? "";
          converted.applicationId = rhinoObj.Id.ToString();

          var objWrapper = new SpeckleObjectWrapper()
          {
            Base = converted,
            GeometryBase = rhinoObj.Geometry,
            Name = rhinoObj.Name ?? "",
            /*Color = GetObjectColor(rhinoObj),
            Material = GetObjectMaterial(rhinoObj),*/
            WrapperGuid = rhinoObj.Id.ToString(),
            ApplicationId = rhinoObj.Id.ToString()
          };

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

  // TODO: claire Icon for speckle param block instance
  //protected override Bitmap Icon => Resources.speckle_param_block_definition;

  public bool IsBakeCapable => !VolatileData.IsEmpty;
  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => throw new NotImplementedException();

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids) =>
    throw new NotImplementedException();

  public void DrawViewportWires(IGH_PreviewArgs args) => throw new NotImplementedException();

  public void DrawViewportMeshes(IGH_PreviewArgs args) => throw new NotImplementedException();

  public bool Hidden { get; set; }
  public BoundingBox ClippingBox { get; }
}
