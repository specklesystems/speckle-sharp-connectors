using Grasshopper.Kernel;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpeckleUrlModelResourceParam : GH_Param<SpeckleUrlModelResourceGoo>
{
  public SpeckleUrlModelResourceParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleUrlModelResourceParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleUrlModelResourceParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleUrlModelResourceParam(GH_ParamAccess access)
    : base("Speckle URL", "spcklUrl", "A Speckle resource", "Speckle", "Resources", access) { }

  public override Guid ComponentGuid => new Guid("E5421FC2-F10D-447F-BF23-5C934ABDB2D3");
}
