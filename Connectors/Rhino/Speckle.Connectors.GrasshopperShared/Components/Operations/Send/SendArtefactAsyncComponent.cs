using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

/// <summary>
/// Asynchronous Publish for Speckle 4.0. Writes an artefact bundle only.
/// </summary>
/// <remarks>Model-wide properties ride eav.model [ENG-9290].</remarks>
[Guid("5D0069FF-827E-4D43-8087-6F7F5C70812F")]
public class SendArtefactAsyncComponent : SendAsyncComponentBase
{
  public override Guid ComponentGuid => GetType().GUID;

  // TODO: needs its own icon - currently shares the deprecated component's, so the two look alike on canvas
  protected override Bitmap Icon => Resources.speckle_operations_publish;

  // mirrors what the deprecated component had before it was hidden
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  public override bool UseArtifacts => true;
  protected override bool HasModelPropertiesInput => true;

  public SendArtefactAsyncComponent()
    : base(
      "Publish",
      "P",
      "Publish a collection to Speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    ) { }
}
