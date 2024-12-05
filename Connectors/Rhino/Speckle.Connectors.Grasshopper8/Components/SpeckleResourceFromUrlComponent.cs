using Grasshopper.Kernel;
using Speckle.Connectors.Grasshopper8.Components.BaseComponents;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Connectors.Grasshopper8.Parameters;

namespace Speckle.Connectors.Grasshopper8.Components;

public class SpeckleResourceFromUrlComponent : SpeckleTaskCapableComponent<string, SpeckleUrlModelResource[]>
{
  public SpeckleResourceFromUrlComponent()
    : base("Speckle Resource From Url", "spcklUrl", "Speckle resource from url", "Speckle", "Resources") { }

  public override Guid ComponentGuid => new("A55C74C6-D955-4822-84BB-2266A2B965EE");

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    pManager.AddTextParameter("URL", "URL", "URL to send to resource", GH_ParamAccess.item);
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam(GH_ParamAccess.list));
  }

  protected override string GetInput(IGH_DataAccess da)
  {
    string url = string.Empty;
    da.GetData(0, ref url);
    return url;
  }

  protected override void SetOutput(IGH_DataAccess da, SpeckleUrlModelResource[] result)
  {
    da.SetDataList(0, result);
  }

  protected override Task<SpeckleUrlModelResource[]> PerformTask(
    string input,
    CancellationToken cancellationToken = default
  )
  {
    var resources = SpeckleResourceBuilder.FromUrlString(input);

    // TODO: Here's where we can validate the resources and throw or not?

    return Task.FromResult(resources);
  }
}
