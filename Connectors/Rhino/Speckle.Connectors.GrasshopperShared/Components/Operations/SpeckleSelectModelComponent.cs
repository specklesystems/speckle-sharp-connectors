using GH_IO.Serialization;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class SpeckleSelectModelComponent : GH_Component
{
  private Account? _account;

  private bool _justPastedIn;

  private string? _storedUserId;
  private string? _storedServer;
  private string? _storedWorkspaceId;
  private string? _storedProjectId;
  private string? _storedModelId;
  private string? _storedVersionId;

  private readonly AccountService _accountService;
  private readonly AccountManager _accountManager;
  private readonly IClientFactory _clientFactory;

  public override Guid ComponentGuid => new("9638B3B5-C469-4570-B69F-686D8DA5C48D");

  public GhContextMenuButton WorkspaceContextMenuButton { get; }
  public GhContextMenuButton ProjectContextMenuButton { get; }
  public GhContextMenuButton ModelContextMenuButton { get; }
  public GhContextMenuButton VersionContextMenuButton { get; }

  private SpeckleOperationWizard SpeckleOperationWizard { get; }

  protected override Bitmap Icon => Resources.speckle_inputs_model;

  public SpeckleSelectModelComponent()
    : base(
      "Speckle Model URL",
      "URL",
      "User selectable model from Speckle",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.OPERATIONS
    )
  {
    Attributes = new SpeckleSelectModelComponentAttributes(this);
    _accountService = PriorityLoader.Container.GetRequiredService<AccountService>();
    _accountManager = PriorityLoader.Container.GetRequiredService<AccountManager>();
    _clientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();

    // TODO: fix this default behavior, use `userSelectedAccountId`
    var account = _accountManager.GetDefaultAccount();
    OnAccountSelected(account);
    SpeckleOperationWizard = new SpeckleOperationWizard(account!, RefreshComponent, false); // TODO: Nullability of account need to be handled before

    WorkspaceContextMenuButton = SpeckleOperationWizard.WorkspaceMenuHandler.WorkspaceContextMenuButton;
    ProjectContextMenuButton = SpeckleOperationWizard.ProjectMenuHandler.ProjectContextMenuButton;
    ModelContextMenuButton = SpeckleOperationWizard.ModelMenuHandler.ModelContextMenuButton;
    VersionContextMenuButton = SpeckleOperationWizard!.VersionMenuHandler!.VersionContextMenuButton; // TODO: fix this shit later when we split
  }

  private Task RefreshComponent()
  {
    ExpireSolution(true);
    return Task.CompletedTask;
  }

  private void OnAccountSelected(Account? account)
  {
    _account = account;
    Message = _account != null ? $"{_account.serverInfo.url}\n{_account.userInfo.email}" : null;
    SpeckleOperationWizard?.SetAccount(account);
  }

  protected override void RegisterInputParams(GH_InputParamManager pManager)
  {
    var urlIndex = pManager.AddTextParameter("Speckle Url", "Url", "Speckle URL", GH_ParamAccess.item);
    pManager[urlIndex].Optional = true;
  }

  protected override void RegisterOutputParams(GH_OutputParamManager pManager)
  {
    pManager.AddParameter(new SpeckleUrlModelResourceParam());
  }

#pragma warning disable CA1502
  protected override void SolveInstance(IGH_DataAccess da)
#pragma warning restore CA1502
  {
    // Deal with inputs
    string? urlInput = null;

    // OPTION 1: Component has input wire connected
    if (da.GetData(0, ref urlInput))
    {
      //Lock button interactions before anything else, to ensure any input (even invalid ones) lock the state.
      SetComponentButtonsState(false);

      if (urlInput == null || string.IsNullOrEmpty(urlInput))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input url was empty or null");
        return;
      }

      try
      {
        var resource = SolveInstanceWithUrlInput(urlInput);
        da.SetData(0, resource);
      }
      catch (SpeckleException e)
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
      }
      return; // Fast exit!
    }

    // OPTION 2: Component is running with no wires connected to input.

    // Unlock button interactions when no input data is provided (no wires connected)
    SetComponentButtonsState(true);

    if (_justPastedIn && _storedUserId != null && !string.IsNullOrEmpty(_storedUserId))
    {
      try
      {
        var account = _accountManager.GetAccount(_storedUserId);
        OnAccountSelected(account);
      }
      catch (SpeckleAccountManagerException e)
      {
        // Swallow and move onto checking server.
        Console.WriteLine(e);
      }

      if (_storedServer != null && _account == null)
      {
        var account = _accountService.GetAccountWithServerUrlFallback(_storedUserId ?? "", new Uri(_storedServer));
        OnAccountSelected(account);
      }
    }

    // Validate backing data
    if (_account == null)
    {
      AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please select an account in the right click menu");
      ProjectContextMenuButton.Enabled = false;
      ModelContextMenuButton.Enabled = false;
      VersionContextMenuButton.Enabled = false;
      return;
    }

    IClient client = _clientFactory.Create(_account);

    var workspaces = client.ActiveUser.GetWorkspaces(10, null, null).Result;
    SpeckleOperationWizard.SetWorkspaces(workspaces);

    // TODO: select default workspace
    var activeWorkspace = client.ActiveUser.GetActiveWorkspace().Result;
    Workspace? selectedWorkspace =
      SpeckleOperationWizard.SelectedWorkspace
      ?? activeWorkspace
      ?? (workspaces.items.Count > 0 ? workspaces.items[0] : null);

    if (selectedWorkspace != null)
    {
      _storedWorkspaceId = selectedWorkspace.id;
      SpeckleOperationWizard.SetDefaultWorkspace(selectedWorkspace);
    }
    else
    {
      return;
    }

    var projects = client
      .ActiveUser.GetProjectsWithPermissions(10, null, new UserProjectsFilter(workspaceId: _storedWorkspaceId))
      .Result;
    SpeckleOperationWizard?.SetProjects(projects);
    ProjectContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedProjectId))
    {
      var project = client.Project.Get(_storedProjectId!).Result;
      // TODO: need to set
      SpeckleOperationWizard?.ProjectMenuHandler.RedrawMenuButton(project);
    }

    if (SpeckleOperationWizard?.SelectedProject == null)
    {
      ModelContextMenuButton.Enabled = false;
      VersionContextMenuButton.Enabled = false;
      return;
    }

    var models = client.Project.GetWithModels(SpeckleOperationWizard.SelectedProject.id, 10).Result.models;
    SpeckleOperationWizard.SetModels(models);
    ModelContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedModelId))
    {
      var model = client.Model.Get(_storedModelId!, SpeckleOperationWizard.SelectedProject.id).Result;
      // TODO: need to set
      SpeckleOperationWizard.ModelMenuHandler.RedrawMenuButton(model);
    }

    if (SpeckleOperationWizard.SelectedModel == null)
    {
      VersionContextMenuButton.Enabled = false;
      return;
    }

    var versions = client
      .Model.GetWithVersions(SpeckleOperationWizard.SelectedModel.id, SpeckleOperationWizard.SelectedProject.id, 10)
      .Result.versions;
    SpeckleOperationWizard.SetVersions(versions);
    VersionContextMenuButton.Enabled = true;

    if (_justPastedIn && !string.IsNullOrEmpty(_storedVersionId))
    {
      var version = client.Version.Get(_storedVersionId!, SpeckleOperationWizard.SelectedProject.id).Result;
      // TODO: need to set
      SpeckleOperationWizard?.VersionMenuHandler?.RedrawMenuButton(version);
    }

    if (SpeckleOperationWizard!.SelectedVersion == null)
    {
      // If no version selected, output `latest` resource
      da.SetData(
        0,
        new SpeckleUrlLatestModelVersionResource(
          _account.serverInfo.url,
          SpeckleOperationWizard.SelectedProject.id,
          SpeckleOperationWizard.SelectedModel.id
        )
      );
      return;
    }

    // If all data points are selected, output specific version.
    da.SetData(
      0,
      new SpeckleUrlModelVersionResource(
        _account.serverInfo.url,
        SpeckleOperationWizard.SelectedProject.id,
        SpeckleOperationWizard.SelectedModel.id,
        SpeckleOperationWizard.SelectedVersion.id
      )
    );
  }

  protected override void AfterSolveInstance()
  {
    // If the component runs once till the end, then it's no longer "just pasted in".
    _justPastedIn = false;
    base.AfterSolveInstance();
  }

  private void SetComponentButtonsState(bool enabled)
  {
    WorkspaceContextMenuButton.Enabled = enabled;
    ProjectContextMenuButton.Enabled = enabled;
    ModelContextMenuButton.Enabled = enabled;
    VersionContextMenuButton.Enabled = enabled;
  }

  private SpeckleUrlModelResource SolveInstanceWithUrlInput(string urlInput)
  {
    // When input is provided, lock interaction of buttons so only text is shown (no context menu)
    // Should perform validation, fill in all internal data of the component (project, model, version, account)
    // Should notify user if any of this goes wrong.

    var resources = SpeckleResourceBuilder.FromUrlString(urlInput);
    if (resources.Length == 0)
    {
      throw new SpeckleException($"Input url string was empty");
    }

    if (resources.Length > 1)
    {
      throw new SpeckleException($"Input multi-model url is not supported");
    }

    var resource = resources.First();

    var account = _accountService.GetAccountWithServerUrlFallback(string.Empty, new Uri(resource.Server));
    OnAccountSelected(account);

    if (_account == null)
    {
      throw new SpeckleException("No account found for server URL");
    }

    IClient client = _clientFactory.Create(_account);

    //var project = client.Project.Get(resource.ProjectId).Result;
    //OnProjectSelected(project, false);

    switch (resource)
    {
      case SpeckleUrlLatestModelVersionResource latestVersionResource:
        var model = client.Model.Get(latestVersionResource.ModelId, latestVersionResource.ProjectId).Result;
        SpeckleOperationWizard?.ModelMenuHandler.RedrawMenuButton(model);
        break;
      case SpeckleUrlModelVersionResource versionResource:
        var m = client.Model.Get(versionResource.ModelId, versionResource.ProjectId).Result;
        SpeckleOperationWizard?.ModelMenuHandler.RedrawMenuButton(m);

        // TODO: this wont be the case when we have separation between send and receive components
        var v = client.Version.Get(versionResource.VersionId, versionResource.ProjectId).Result;
        SpeckleOperationWizard?.VersionMenuHandler?.RedrawMenuButton(v);
        break;
      case SpeckleUrlModelObjectResource:
        throw new SpeckleException("Object URLs are not supported");
      default:
        throw new SpeckleException("Unknown Speckle resource type");
    }

    return resource;
  }

  public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
  {
    base.AppendAdditionalMenuItems(menu);
    var accountsMenu = Menu_AppendItem(menu, "Account");

    foreach (var account in _accountManager.GetAccounts())
    {
      Menu_AppendItem(
        accountsMenu.DropDown,
        account.ToString(),
        (_, _) => OnAccountSelected(account),
        null,
        _account?.id != account.id,
        _account?.id == account.id
      );
    }
  }

  public override bool Write(GH_IWriter writer)
  {
    var baseRes = base.Write(writer);
    writer.SetString("Server", _account?.serverInfo.url);
    writer.SetString("User", _account?.id);
    writer.SetString("Project", SpeckleOperationWizard?.SelectedProject?.id);
    writer.SetString("Model", SpeckleOperationWizard?.SelectedModel?.id);
    writer.SetString("Version", SpeckleOperationWizard?.SelectedVersion?.id);

    return baseRes;
  }

  public override bool Read(GH_IReader reader)
  {
    var readRes = base.Read(reader);

    reader.TryGetString("Server", ref _storedServer);
    reader.TryGetString("User", ref _storedUserId);
    reader.TryGetString("Project", ref _storedProjectId);
    reader.TryGetString("Model", ref _storedModelId);
    reader.TryGetString("Version", ref _storedVersionId);

    _justPastedIn = true;
    return readRes;
  }

  public override void ExpirePreview(bool redraw)
  {
    ProjectContextMenuButton.ExpirePreview(redraw);
    ModelContextMenuButton.ExpirePreview(redraw);
    VersionContextMenuButton.ExpirePreview(redraw);
    base.ExpirePreview(redraw);
  }
}
