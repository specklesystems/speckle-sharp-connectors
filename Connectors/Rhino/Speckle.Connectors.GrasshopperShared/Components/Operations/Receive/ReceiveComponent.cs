using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

/// <summary>
/// Deprecated synchronous Load. Kept working, unchanged, for scripts authored before 4.0 - see
/// <see cref="ReceiveComponentBase.PreferArtefacts"/>.
/// </summary>
public class ReceiveComponent : ReceiveComponentBase
{
  public override Guid ComponentGuid => new("74954F59-B1B7-41FD-97DE-4C6B005F2801");
  protected override Bitmap Icon => Resources.speckle_operations_syncload;
  public override GH_Exposure Exposure => GH_Exposure.hidden;

  /// <remarks>
  /// Marks this component as obsolete in the Grasshopper UI (hides it from the ribbon, adds the
  /// "obsolete" overlay icon on canvas).
  /// </remarks>
  public override bool Obsolete => true;

  protected override bool PreferArtefacts => false;

  public ReceiveComponent()
    : base(
      // display name only - Grasshopper binds by ComponentGuid, so this is cosmetic and safe to change
      "(Sync) Load (legacy)",
      "sL",
      "Load a model from Speckle, synchronously. Deprecated - use the new (Sync) Load component.",
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
      "Model-wide properties from the root collection",
      GH_ParamAccess.item
    );

    pManager.AddParameter(
      new SpecklePropertyGroupParam(),
      "Proxies",
      "proxies",
      "Proxy objects from the root collection, keyed by type (e.g. levelProxies, analysisResults). Use Deconstruct to access individual lists.",
      GH_ParamAccess.item
    );
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, Constants.DEPRECATED_LOAD_MESSAGE);
    base.SolveInstance(da);
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
    da.SetData(2, result.ProxiesGoo);
    SetStatusMessage(true);
  }
}
