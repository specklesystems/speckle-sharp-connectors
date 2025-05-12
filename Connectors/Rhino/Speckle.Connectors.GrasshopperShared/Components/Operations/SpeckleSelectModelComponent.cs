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

    SpeckleOperationWizard = new SpeckleOperationWizard(RefreshComponent, false);
    _account = SpeckleOperationWizard.SelectedAccount;
    UpdateMessageWithAccount(_account);

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

  private void UpdateMessageWithAccount(Account? account) =>
    Message = account != null ? $"{account.serverInfo.url}\n{account.userInfo.email}" : null;

  private void OnAccountSelected(Account? account)
  {
    _account = account;
    _storedUserId = _account?.id;
    _storedServer = _account?.serverInfo.url;
    Message = _account != null ? $"{_account.serverInfo.url}\n{_account.userInfo.email}" : null;
    SpeckleOperationWizard.SetAccount(_account);
    ExpireSolution(true);
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

  private string? UrlInput { get; set; }

#pragma warning disable CA1502
  protected override void SolveInstance(IGH_DataAccess da)
#pragma warning restore CA1502
  {
    // Deal with inputs
    string? urlInput = null;

    // OPTION 1: Component has input wire connected
    if (da.GetData(0, ref urlInput))
    {
      UrlInput = urlInput;
      //Lock button interactions before anything else, to ensure any input (even invalid ones) lock the state.
      SpeckleOperationWizard.SetComponentButtonsState(false);

      if (urlInput == null || string.IsNullOrEmpty(urlInput))
      {
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input url was empty or null");
        return;
      }

      try
      {
        // NOTE: once we split the logic in Sender and Receiver components, we need to set flag correctly
        var (resource, accountId, hasPermission) = SpeckleOperationWizard.SolveInstanceWithUrlInput(urlInput, true);
        if (!hasPermission)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "You do not have enough permission for this project.");
        }
        _storedUserId = accountId;
        _storedServer = resource.Server;
        UpdateMessageWithAccount(SpeckleOperationWizard.SelectedAccount);
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
    SpeckleOperationWizard.SetComponentButtonsState(true);

    // When user unplugs the URL input, we need to reset all first
    if (UrlInput != null)
    {
      UrlInput = null;
      SpeckleOperationWizard.WorkspaceMenuHandler.Reset();
      SpeckleOperationWizard.ProjectMenuHandler.Reset();
      SpeckleOperationWizard.ModelMenuHandler.Reset();
      SpeckleOperationWizard.VersionMenuHandler?.Reset();
      _account = SpeckleOperationWizard.SelectedAccount;
      _storedUserId = _account?.id;
    }

    if (_justPastedIn && _storedUserId != null && !string.IsNullOrEmpty(_storedUserId))
    {
      try
      {
        var account = _accountManager.GetAccount(_storedUserId);
        _account = account;
        SpeckleOperationWizard.SetAccount(account);
        UpdateMessageWithAccount(SpeckleOperationWizard.SelectedAccount);
      }
      catch (SpeckleAccountManagerException e)
      {
        // Swallow and move onto checking server.
        Console.WriteLine(e);
      }

      if (_storedServer != null && _account == null)
      {
        var account = _accountService.GetAccountWithServerUrlFallback(_storedUserId ?? "", new Uri(_storedServer));
        _account = account;
        SpeckleOperationWizard.SetAccount(account);
        UpdateMessageWithAccount(SpeckleOperationWizard.SelectedAccount);
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

    // NOTE FOR LATER: Need to be handled in SDK... and will come later by Jeddward Morgan...
    if (SpeckleOperationWizard.WorkspaceMenuHandler.Workspaces == null)
    {
      var workspaces = client.ActiveUser.GetWorkspaces(10, null, null).Result;
      if (workspaces.items.Count == 0)
      {
        // Create a workspace flow
        SpeckleOperationWizard.CreateNewWorkspaceUIState();
        return;
      }
      SpeckleOperationWizard.SetWorkspaces(workspaces);
    }

    if (
      SpeckleOperationWizard.SelectedWorkspace == null
      && SpeckleOperationWizard.WorkspaceMenuHandler.IsPersonalProjects
    )
    {
      _storedWorkspaceId = null;
    }
    else
    {
      var activeWorkspace = client.ActiveUser.GetActiveWorkspace().Result;
      Workspace? selectedWorkspace =
        SpeckleOperationWizard.SelectedWorkspace
        ?? activeWorkspace
        ?? (
          SpeckleOperationWizard.WorkspaceMenuHandler?.Workspaces?.items.Count > 0
            ? SpeckleOperationWizard.WorkspaceMenuHandler?.Workspaces?.items[0]
            : null
        );

      if (selectedWorkspace != null)
      {
        _storedWorkspaceId = selectedWorkspace.id;
        SpeckleOperationWizard.SetDefaultWorkspace(selectedWorkspace);
      }
      else
      {
        return;
      }
    }

    var projects = client
      .ActiveUser.GetProjectsWithPermissions(
        10,
        null,
        new UserProjectsFilter(
          workspaceId: _storedWorkspaceId,
          includeImplicitAccess: true,
          personalOnly: SpeckleOperationWizard.WorkspaceMenuHandler?.IsPersonalProjects
        )
      )
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
        SpeckleOperationWizard.SelectedAccount?.id != account.id,
        SpeckleOperationWizard.SelectedAccount?.id == account.id
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
