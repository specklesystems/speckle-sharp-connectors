using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;

namespace Speckle.Connectors.GrasshopperShared.Components.Dev;

[Guid("18152AE4-4BE7-46F0-9826-09061897A5CC")]
public class TokenUrlComponent : GH_Component
{
  public TokenUrlComponent()
    : base(
      "Speckle Model URL",
      "URL",
      "Create a Speckle model link using URL and developer token",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.DEVELOPER
    ) { }

  public override Guid ComponentGuid => GetType().GUID;
  protected override Bitmap Icon => Resources.speckle_inputs_model;

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddTextParameter("Speckle Url", "Url", "Speckle URL", GH_ParamAccess.item);
    pManager.AddTextParameter("Speckle Token", "Token", "Speckle Authorization Token", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
  }

  protected override void SolveInstance(IGH_DataAccess da)
  {
    // get inputs
    string urlInput = "";
    if (!da.GetData(0, ref urlInput))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Speckle Url is missing");
      return;
    }

    string tokenInput = "";
    if (!da.GetData(0, ref tokenInput))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Speckle token is missing");
      return;
    }

    // do work here

    // output url resource
    da.SetData(0, new SpeckleUrlModelVersionResource());
  }
}
