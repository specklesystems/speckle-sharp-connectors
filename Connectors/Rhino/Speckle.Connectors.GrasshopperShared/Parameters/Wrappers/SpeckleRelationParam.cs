using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Carries <see cref="SpeckleRelationGoo"/> between the Speckle Relation component and Publish. Hidden from the ribbon:
/// a relation is authored by its component, never typed in by hand.
/// </summary>
public class SpeckleRelationParam : GH_Param<SpeckleRelationGoo>
{
  public SpeckleRelationParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleRelationParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleRelationParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleRelationParam(GH_ParamAccess access)
    : base(
      "Speckle Relation",
      "SR",
      "Represents a relation between two Speckle objects",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("7D2E4C1A-9B3F-4E6A-8C5D-2F1B0A9E7C43");
  protected override Bitmap Icon => Resources.speckle_objects_query;
  public override GH_Exposure Exposure => GH_Exposure.hidden;
}
