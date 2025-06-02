using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a block instance.
/// </summary>
public class SpeckleBlockInstanceWrapper : SpeckleWrapper
{
  public override required Base Base { get; set; }
}

public class SpeckleBlockInstanceWrapperGoo : GH_Goo<SpeckleBlockInstanceWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Block Instance Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle block instance wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper block instances.";

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
