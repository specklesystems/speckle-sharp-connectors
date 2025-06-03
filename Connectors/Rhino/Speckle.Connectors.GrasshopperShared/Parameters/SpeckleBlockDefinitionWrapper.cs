using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

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
      if (value is not InstanceDefinitionProxy instanceDefinition)
      {
        throw new ArgumentException("InstanceDefinitionProxy is not a valid instance definition.");
      }

      InstanceDefinitionProxy = instanceDefinition;
    }
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
