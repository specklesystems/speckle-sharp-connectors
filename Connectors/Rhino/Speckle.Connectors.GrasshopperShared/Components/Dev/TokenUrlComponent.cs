using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;

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
  protected override Bitmap Icon => Resources.speckle_inputs_token;

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
    if (!da.GetData(1, ref tokenInput))
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Speckle token is missing");
      return;
    }

    try
    {
      // NOTE: once we split the logic in Sender and Receiver components, we need to set flag correctly
      var (resource, hasPermission) = SolveInstanceWithUrAndToken(urlInput, tokenInput, true);
      if (!hasPermission)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "You do not have enough permission for this project.");
      }
      da.SetData(0, resource);
    }
    catch (SpeckleException e)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
      da.AbortComponentSolution();
    }
  }

  public (SpeckleUrlModelResource resource, bool hasPermission) SolveInstanceWithUrAndToken(
    string input,
    string token,
    bool isSender
  )
  {
    // When input is provided, lock interaction of buttons so only text is shown (no context menu)
    // Should perform validation, fill in all internal data of the component (project, model, version, account)
    // Should notify user if any of this goes wrong.

    var resources = SpeckleResourceBuilder.FromUrlString(input, token);
    if (resources.Length != 1)
    {
      // POC: this shouldn't ever hit since exceptions are thrown in the FromUrlString method
      throw new SpeckleException($"FromUrlString parser returned an invalid resource");
    }

    var resource = resources.First();
    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var account = resource.Account.GetAccount(scope);
    if (account != null)
    {
      scope.Get<IAccountService>().SetUserSelectedAccountId(account.id);
    }
    else
    {
      throw new SpeckleException("No account found for server URL");
    }

    IClient client = scope.Get<IClientFactory>().Create(account);

    var project = client.Project.Get(resource.ProjectId).Result;
    var projectPermissions = client.Project.GetPermissions(resource.ProjectId).Result;
    if (project != null && project.workspaceId != null)
    {
      var workspace = client.Workspace.Get(project.workspaceId).Result;
    }

    switch (resource)
    {
      case SpeckleUrlLatestModelVersionResource latestVersionResource:
        var model = client.Model.Get(latestVersionResource.ModelId, latestVersionResource.ProjectId).Result;
        break;
      case SpeckleUrlModelVersionResource versionResource:
        var m = client.Model.Get(versionResource.ModelId, versionResource.ProjectId).Result;
        // TODO: this wont be the case when we have separation between send and receive components
        var v = client.Version.Get(versionResource.VersionId, versionResource.ProjectId).Result;
        break;
      case SpeckleUrlModelObjectResource:
        throw new SpeckleException("Object URLs are not supported");
      default:
        throw new SpeckleException("Unknown Speckle resource type");
    }

    return (resource, isSender ? projectPermissions.canPublish.authorized : projectPermissions.canLoad.authorized);
  }
}
