using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components;

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
    : base(
      "Model Link",
      "SML",
      "A resource link to a Speckle Model",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new Guid("E5421FC2-F10D-447F-BF23-5C934ABDB2D3");

  // hide this param since we don't do anything with it
  public override GH_Exposure Exposure => GH_Exposure.hidden;
}
