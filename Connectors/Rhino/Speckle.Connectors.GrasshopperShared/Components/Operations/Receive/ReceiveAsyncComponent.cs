using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Receive;

/// <summary>
/// Deprecated asynchronous Load. Kept working, unchanged, for scripts authored before 4.0 - see
/// <see cref="ReceiveAsyncComponentBase.PreferArtefacts"/>.
/// </summary>
[Guid("1587DF34-83E5-4AFE-B42E-F7C5C37ECD68")]
public class ReceiveAsyncComponent : ReceiveAsyncComponentBase
{
  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_operations_load;
  public override GH_Exposure Exposure => GH_Exposure.hidden;

  /// <remarks>
  /// Marks this component as obsolete in the Grasshopper UI (hides it from the ribbon, adds the
  /// "obsolete" overlay icon on canvas).
  /// </remarks>
  public override bool Obsolete => true;

  public override bool PreferArtefacts => false;

  public ReceiveAsyncComponent()
    : base(
      // display name only - Grasshopper binds by ComponentGuid, so this is cosmetic and safe to change
      "Load (legacy)",
      "L",
      "Load a model from Speckle. Deprecated - use the new Load component.",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
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

  public override void WriteOutputs(IGH_DataAccess da, ReceiveComponentOutput result)
  {
    da.SetData(0, result.RootObject);
    da.SetData(1, result.RootProperties);
    da.SetData(2, result.ProxiesGoo);
  }
}
