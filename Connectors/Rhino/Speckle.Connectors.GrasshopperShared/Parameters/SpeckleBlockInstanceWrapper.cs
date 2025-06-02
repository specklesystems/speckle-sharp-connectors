using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// A Wrapper class representing a block instance.
/// </summary>
/// <remarks>
/// Some cool remarks ... ⌛️
/// </remarks>
public class SpeckleBlockInstanceWrapper : SpeckleWrapper
{
  private InstanceProxy InstanceProxy { get; set; } // NOTE: stores the actual typed object from `Base`
  public override required Base Base // NOTE: `InstanceProxy` wraps `Base` just like `SpeckleCollectionWrapper` and `SpeckleMaterialWrapper`
  {
    get => InstanceProxy;
    set
    {
      if (value is not InstanceProxy proxy)
      {
        throw new ArgumentException("Cannot create block instance wrapper from a non-InstanceProxy Base");
      }

      InstanceProxy = proxy;
    }
  }

  // TODO: blocked by [CNX-1941](https://linear.app/speckle/issue/CNX-1941/add-speckle-blockdefinition-param)
  //public SpeckleBlockDefinitionGoo? Definition { get; set; }

  public Transform Transform { get; set; } = Transform.Identity;

  public SpecklePropertyGoo Properties { get; set; } = new();

  // TODO: we need to wait on this. not sure how to tackle this 🤯 overrides etc.
  /*public Color? Color { get; set; }
  public SpeckleMaterialWrapper? Material { get; set; }*/

  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false) => throw new NotImplementedException();

  public void Bake(RhinoDoc doc, List<Guid> blockIds, int bakeLayerIndex = -1, bool layersAlreadyCreated = false) =>
    throw new NotImplementedException();
}

public class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Block Instance Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle block instance wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper block instances.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleBlockInstanceWrapper wrapper:
        Value = wrapper;
        return true;
      case GH_Goo<SpeckleBlockInstanceWrapper> wrapperGoo:
        Value = wrapperGoo.Value;
        return true;
      case Transform transform:
        Value = new SpeckleBlockInstanceWrapper()
        {
          // TODO: blocked by [CNX-1941](https://linear.app/speckle/issue/CNX-1941/add-speckle-blockdefinition-param)
          // Base = new InstanceProxy() { ... }
          Base = new Base(),
          Transform = transform,
          ApplicationId = Guid.NewGuid().ToString()
        };
        return true;
    }
    return false;
  }

  public override bool CastTo<T>(ref T target)
  {
    if (Value == null)
    {
      return false;
    }

    var type = typeof(T);

    if (type == typeof(Transform))
    {
      target = (T)(object)Value.Transform;
      return true;
    }
    return false;
  }

  public void DrawViewportWires(GH_PreviewWireArgs args) => throw new NotImplementedException();

  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => throw new NotImplementedException();

  public BoundingBox ClippingBox { get; }
}

public class SpeckleBlockInstanceParameters
  : GH_Param<SpeckleBlockInstanceWrapperGoo>,
    IGH_BakeAwareObject,
    IGH_PreviewObject
{
  public SpeckleBlockInstanceParameters(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleBlockInstanceParameters(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleBlockInstanceParameters(GH_ParamAccess access)
    : base(
      "Speckle Block Instance", // TODO: claire & bjorn to discuss this wording
      "SI", // TODO: claire & bjorn to discuss this wording
      "Represents a Speckle block instance",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("938CCD6E-B202-4A0C-9D68-ABD7683B0EDE");

  // TODO: claire Icon for speckle param block instance
  //protected override Bitmap Icon => Resources.speckle_param_block_instance;

  public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids) => throw new NotImplementedException();

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids) =>
    throw new NotImplementedException();

  public bool IsBakeCapable => !VolatileData.IsEmpty;

  public void DrawViewportWires(IGH_PreviewArgs args) => throw new NotImplementedException();

  public void DrawViewportMeshes(IGH_PreviewArgs args) => throw new NotImplementedException();

  public bool Hidden { get; set; }
  public bool IsPreviewCapable => !VolatileData.IsEmpty;
  public BoundingBox ClippingBox { get; }
}
