using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block definition and its converted Speckle equivalent.
/// </summary>
public class SpeckleBlockDefinitionWrapper : SpeckleWrapper
{
  private InstanceDefinitionProxy InstanceDefinitionProxy { get; set; }

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
}

public class SpeckleBlockDefinitionWrapperGoo : GH_Goo<SpeckleBlockDefinitionWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public void DrawViewportWires(GH_PreviewWireArgs args) => throw new NotImplementedException();

  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => throw new NotImplementedException();

  public BoundingBox ClippingBox { get; }

  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Block Definition Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle block definition";
  public override string TypeDescription => "A wrapper around speckle grasshopper block definitions.";
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
