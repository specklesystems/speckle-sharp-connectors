using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

/// <summary>
/// Deprecated asynchronous Publish. Still writes a v3 version so teammates on older connectors can read it - see
/// <see cref="SendAsyncComponentBase.UseArtifacts"/>.
/// </summary>
[Guid("52481972-7867-404F-8D9F-E1481183F355")]
public class SendAsyncComponent : SendAsyncComponentBase
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_operations_publish;
  public override GH_Exposure Exposure => GH_Exposure.hidden;

  /// <remarks>
  /// Marks this component as obsolete in the Grasshopper UI (hides it from the ribbon, adds the
  /// "obsolete" overlay icon on canvas).
  /// </remarks>
  public override bool Obsolete => true;

  public override bool UseArtifacts => false;
  protected override bool HasModelPropertiesInput => true;

  public SendAsyncComponent()
    : base(
      // display name only - Grasshopper binds by ComponentGuid, so this is cosmetic and safe to change
      "Publish (legacy)",
      "P",
      "Publish a collection to Speckle. Deprecated - use the new Publish component.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    ) { }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, Constants.DEPRECATED_PUBLISH_MESSAGE);
    base.SolveInstance(da);
  }

  public override void OnPublished() =>
    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Constants.PUBLISHED_LEGACY_VERSION_MESSAGE);
}
