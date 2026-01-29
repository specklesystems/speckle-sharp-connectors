using System.Runtime.InteropServices;
using Grasshopper.Kernel;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Dev;

[Guid("18152AE4-4BE7-46F0-9826-09061897A5CC")]
public class TokenUrlComponent : GH_Component
{
  public TokenUrlComponent()
    : base(
      "Speckle Model URL with Token",
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
    pManager.AddTextParameter(
      "Speckle Token",
      "Token",
      "Speckle Authorization Token. Requires profile:read, profile:email, stream:read, and workspace:read (unless on a non-workspace enable server), as well as any other write scopes needed",
      GH_ParamAccess.item
    );
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
      var resource = SolveInstanceWithUrAndToken(urlInput, tokenInput, true).Result;

      da.SetData(0, resource);
    }
    catch (SpeckleException e)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
      da.AbortComponentSolution();
    }
  }

  private async Task<SpeckleUrlModelResource> SolveInstanceWithUrAndToken(string input, string? token, bool isSender)
  {
    // When input is provided, lock interaction of buttons so only text is shown (no context menu)
    // Should perform validation, fill in all internal data of the component (project, model, version, account)
    // Should notify user if any of this goes wrong.

    var resources = SpeckleResourceBuilder.FromUrlString(input, token);
    if (resources.Length != 1)
    {
      // POC: this shouldn't ever hit since exceptions are thrown in the FromUrlString method
      throw new SpeckleException("FromUrlString parser returned an invalid resource");
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

    if (account.userInfo.id is null || account.userInfo.email is null)
    {
      throw new SpeckleException("Token requires profile:read and profile:email scopes");
    }

    using var userScope = UserActivityScope.AddUserScope(account);
    IClient client = scope.Get<IClientFactory>().Create(account);

    var project = await client.Project.Get(resource.ProjectId);
    var projectPermissions = await client.Project.GetPermissions(resource.ProjectId);
    if (project.workspaceId != null)
    {
      _ = await client.Workspace.Get(project.workspaceId);
    }

    ModelPermissionChecks modelPermissions;
    switch (resource)
    {
      case SpeckleUrlLatestModelVersionResource r:
        modelPermissions = await client.Model.GetPermissions(r.ModelId, r.ProjectId);
        break;
      case SpeckleUrlModelVersionResource r:
        modelPermissions = await client.Model.GetPermissions(r.ModelId, r.ProjectId);

        // TODO: this wont be the case when we have separation between send and receive components
        _ = await client.Version.Get(r.VersionId, r.ProjectId);
        break;
      case SpeckleUrlModelObjectResource:
        throw new SpeckleException("Object URLs are not supported");
      default:
        throw new SpeckleException("Unknown Speckle resource type");
    }

    if (isSender)
    {
      modelPermissions.canCreateVersion.EnsureAuthorised();
    }
    else
    {
      projectPermissions.canLoad.EnsureAuthorised();
    }
    return resource;
  }
}
