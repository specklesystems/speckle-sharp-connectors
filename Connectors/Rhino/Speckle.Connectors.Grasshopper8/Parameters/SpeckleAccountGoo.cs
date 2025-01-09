using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Grasshopper8.Parameters;

public class SpeckleAccountGoo : GH_Goo<Account>
{
  public override IGH_Goo Duplicate() => new SpeckleAccountGoo { m_value = Value };

  public override string ToString() => $"Speckle Account [{m_value.id}]";

  public override bool IsValid => true;
  public override string TypeName => "SpeckleAccountGoo";
  public override string TypeDescription => "Holds a Speckle Account to authenticate with.";
}

public class SpeckleAccountParam : GH_Param<SpeckleAccountGoo>
{
  public SpeckleAccountParam()
    : this("Speckle Account", "SA", "A Speckle account", "SpeckleAccount", "SpeckleAccount", GH_ParamAccess.item) { }

  public SpeckleAccountParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleAccountParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleAccountParam(
    string name,
    string nickname,
    string description,
    string category,
    string subcategory,
    GH_ParamAccess access
  )
    : base(name, nickname, description, category, subcategory, access) { }

  public override Guid ComponentGuid => new("6297E260-C2AD-4391-B772-D503BF6AB7F2");
}
