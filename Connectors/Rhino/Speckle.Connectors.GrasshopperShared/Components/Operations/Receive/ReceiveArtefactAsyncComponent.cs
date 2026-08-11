using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

/// <summary>
/// Asynchronous Load for Speckle 4.0. Reads the artefact bundle whenever a version has one, so its output shape
/// doesn't depend on whether that version happens to have been migrated.
/// </summary>
[Guid("9CA2F082-B706-4CB0-922F-356AE339E434")]
public class ReceiveArtefactAsyncComponent : ReceiveAsyncComponentBase
{
  public override Guid ComponentGuid => GetType().GUID;

  // TODO: needs its own icon - currently shares the deprecated component's, so the two look alike on canvas
  protected override Bitmap Icon => Resources.speckle_operations_load;
  // mirrors what the deprecated component had before it was hidden
  public override GH_Exposure Exposure => GH_Exposure.secondary;

  public override bool PreferArtefacts => true;

  public ReceiveArtefactAsyncComponent()
    : base(
      "Load",
      "L",
      "Load a model from Speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    ) { }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager) =>
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Collection",
      "collection",
      "The model collection of the loaded version",
      GH_ParamAccess.item
    );

  public override void WriteOutputs(IGH_DataAccess da, ReceiveComponentOutput result) =>
    da.SetData(0, result.RootObject);
}
