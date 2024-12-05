using Grasshopper.Kernel;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpeckleObjectParam : GH_Param<SpeckleObjectGoo>
{
  public SpeckleObjectParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleObjectParam(GH_ParamAccess access)
    : base("Speckle Object", "SpklObj", "XXXXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("F708F88C-FE00-44EF-8D30-02AB6CF5F728");
}
