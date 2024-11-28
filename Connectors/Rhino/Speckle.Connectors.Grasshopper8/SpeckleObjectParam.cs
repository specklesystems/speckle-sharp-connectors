using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Grasshopper8;

public class SpeckleObjectParam : GH_Param<SpeckleObjectGoo>
{
  public SpeckleObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleObjectParam(GH_ParamAccess access)
    : base("Speckle Object", "SpklObj", "XXXXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("F708F88C-FE00-44EF-8D30-02AB6CF5F728");
}

public class SpeckleObjectGoo : GH_Goo<ISpeckleObject>
{
  // TODO: Massive hack for setup only!!! We need some sort of `ShallowCopy` or a transparent wrapper for Speckle Objects
  // to prevent backwards propagation of changes of the same instance.
  public override IGH_Goo Duplicate() => new SpeckleObjectGoo { Value = m_value };

  public override string ToString() => m_value.ToString();

  public override bool IsValid => true;
  public override string TypeName => "SpeckleObject";
  public override string TypeDescription => "A Speckle Object";
}
