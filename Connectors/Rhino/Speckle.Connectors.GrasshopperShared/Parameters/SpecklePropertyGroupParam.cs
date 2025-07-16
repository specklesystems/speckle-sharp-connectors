using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public class SpecklePropertyGroupParam : GH_Param<SpecklePropertyGroupGoo>
{
  public override Guid ComponentGuid => new("AF4757C3-BA33-4ACD-A92B-C80356043129");
  protected override Bitmap Icon => Resources.speckle_param_properties;
  public override GH_Exposure Exposure => GH_Exposure.quarternary;

  public SpecklePropertyGroupParam()
    : this(GH_ParamAccess.item) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpecklePropertyGroupParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpecklePropertyGroupParam(GH_ParamAccess access)
    : base(
      "Speckle Properties",
      "SP",
      "Represents a set of Speckle Properties",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }
}
