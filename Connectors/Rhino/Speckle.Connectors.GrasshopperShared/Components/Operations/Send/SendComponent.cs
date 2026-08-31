using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

/// <summary>
/// Deprecated synchronous Publish. Still writes a v3 version so teammates on older connectors can read it - see
/// <see cref="SendComponentBase.UseArtifacts"/>.
/// </summary>
public class SendComponent : SendComponentBase
{
  public override Guid ComponentGuid => new("0CF0D173-BDF0-4AC2-9157-02822B90E9FB");
  protected override Bitmap Icon => Resources.speckle_operations_syncpublish;
  public override GH_Exposure Exposure => GH_Exposure.hidden;

  /// <remarks>
  /// Marks this component as obsolete in the Grasshopper UI (hides it from the ribbon, adds the
  /// "obsolete" overlay icon on canvas).
  /// </remarks>
  public override bool Obsolete => true;

  protected override bool UseArtifacts => false;
  protected override bool HasModelPropertiesInput => true;

  public SendComponent()
    : base(
      // display name only - Grasshopper binds by ComponentGuid, so this is cosmetic and safe to change
      "(Sync) Publish (legacy)",
      "sP",
      "Publish a collection to Speckle, synchronously. Deprecated - use the new (Sync) Publish component.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, Constants.DEPRECATED_PUBLISH_MESSAGE);
    base.SolveInstance(da);
  }

  protected override void OnPublishStarting() =>
    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Constants.PUBLISHED_LEGACY_VERSION_MESSAGE);
}
