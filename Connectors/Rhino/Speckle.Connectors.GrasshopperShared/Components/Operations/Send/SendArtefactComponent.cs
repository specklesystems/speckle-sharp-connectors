using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

/// <summary>
/// Synchronous Publish for Speckle 4.0. Writes an artefact bundle only.
/// </summary>
/// <remarks>
/// No model-wide properties input: the bundle has nowhere to put them, and the sidecar that would have carried them
/// was rejected. Revisit if a root-properties design lands in the bundle spec.
/// </remarks>
[Guid("D69E558E-FCD2-45B4-AB34-EE15F58D0B58")]
public class SendArtefactComponent : SendComponentBase
{
  public override Guid ComponentGuid => GetType().GUID;

  // TODO: needs its own icon - currently shares the deprecated component's, so the two look alike on canvas
  protected override Bitmap Icon => Resources.speckle_operations_syncpublish;

  // the deprecated component had no override, i.e. the default, before it was hidden
  public override GH_Exposure Exposure => GH_Exposure.primary;

  protected override bool UseArtifacts => true;
  protected override bool HasModelPropertiesInput => false;

  public SendArtefactComponent()
    : base(
      "(Sync) Publish",
      "sP",
      "Publish a collection to Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }
}
