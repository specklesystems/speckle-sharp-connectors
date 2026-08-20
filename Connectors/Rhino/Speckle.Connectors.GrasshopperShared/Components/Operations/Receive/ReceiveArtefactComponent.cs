using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

/// <summary>
/// Synchronous Load for Speckle 4.0. Reads the artefact bundle whenever a version has one, so its output shape
/// doesn't depend on whether that version happens to have been migrated.
/// </summary>
[Guid("D0E35B24-D1F8-484B-B2B4-AFB4CA3CEE92")]
public class ReceiveArtefactComponent : ReceiveComponentBase
{
  public override Guid ComponentGuid => GetType().GUID;

  // TODO: needs its own icon - currently shares the deprecated component's, so the two are indistinguishable on canvas
  protected override Bitmap Icon => Resources.speckle_operations_syncload;

  // the deprecated component had no override, i.e. the default, before it was hidden
  public override GH_Exposure Exposure => GH_Exposure.primary;

  protected override bool PreferArtefacts => true;

  public ReceiveArtefactComponent()
    : base(
      "(Sync) Load",
      "sL",
      "Load a model from Speckle, synchronously",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(
      new SpeckleCollectionParam(GH_ParamAccess.item),
      "Collection",
      "collection",
      "The model collection of the loaded version",
      GH_ParamAccess.item
    );

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Properties",
      "properties",
      "Model-wide properties of the loaded version",
      GH_ParamAccess.item
    );
  }

  protected override void SetOutput(IGH_DataAccess da, ReceiveComponentOutput result)
  {
    if (result.RootObject is null)
    {
      SetStatusMessage(false);
      return;
    }

    da.SetData(0, result.RootObject);
    da.SetData(1, result.RootProperties);
    SetStatusMessage(true);
  }
}
