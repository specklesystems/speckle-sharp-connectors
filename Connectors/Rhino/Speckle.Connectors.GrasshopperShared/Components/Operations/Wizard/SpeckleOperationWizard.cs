using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;

/// <summary>
/// Wizard to handle cascading selections in an order Workspace, Project, Model and Version for operations.
/// Wraps the UI components with it and exposes the state of the selection to consumer.
/// </summary>
public class SpeckleOperationWizard
{
  private readonly IClientFactory _clientFactory;

  public Account? SelectedAccount { get; private set; }
  public List<Account>? Accounts { get; }
  public LimitedWorkspace? SelectedWorkspace { get; private set; }
  public Project? SelectedProject { get; private set; }
  public Model? SelectedModel { get; private set; }
  public Version? SelectedVersion { get; private set; }
  public bool IsLatestVersion { get; private set; }

  public WorkspaceMenuHandler WorkspaceMenuHandler { get; }
  public ProjectMenuHandler ProjectMenuHandler { get; }
  public ModelMenuHandler ModelMenuHandler { get; }
  public VersionMenuHandler? VersionMenuHandler { get; }

  private readonly Func<Task> _refreshComponent;
  private readonly Func<string, Task> _updateComponentMessage;
  private readonly IAccountService _accountService;
  private readonly IAccountManager _accountManager;

  /// <param name="refreshComponent"> Callback function to trigger when component need to refresh itself.</param>
  /// <param name="isSender"> Whether it will be used in sender or receiver. Accordingly, the wizard will manage versions or not.</param>
  public SpeckleOperationWizard(Func<Task> refreshComponent, Func<string, Task> updateComponentMessage, bool isSender)
  {
    _refreshComponent = refreshComponent;
    _updateComponentMessage = updateComponentMessage;
    _clientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();
    _accountManager = PriorityLoader.Container.GetRequiredService<IAccountManager>();
    _accountService = PriorityLoader.Container.GetRequiredService<IAccountService>();

    var userSelectedAccountId = _accountService.GetUserSelectedAccountId();
    Accounts = _accountManager.GetAccounts().ToList();
    SelectedAccount = Accounts.FirstOrDefault(a => a.id == userSelectedAccountId);

    WorkspaceMenuHandler = new WorkspaceMenuHandler(FetchWorkspaces, CreateNewWorkspace);
    ProjectMenuHandler = new ProjectMenuHandler(FetchProjects);
    ModelMenuHandler = new ModelMenuHandler(FetchModels);
    if (!isSender)
    {
      VersionMenuHandler = new VersionMenuHandler(FetchVersions);
      VersionMenuHandler.VersionSelected += OnVersionSelected;
    }

    WorkspaceMenuHandler.WorkspaceSelected += OnWorkspaceSelected;
    ProjectMenuHandler.ProjectSelected += OnProjectSelected;
    ModelMenuHandler.ModelSelected += OnModelSelected;
  }

  public (SpeckleUrlModelResource resource, bool hasPermission) SolveInstanceWithUrlInput(
    string input,
    bool isSender,
    string? token
  )
  {
    // When input is provided, lock interaction of buttons so only text is shown (no context menu)
    // Should perform validation, fill in all internal data of the component (project, model, version, account)
    // Should notify user if any of this goes wrong.

    var resources = SpeckleResourceBuilder.FromUrlString(input, token);
    if (resources.Length == 0)
    {
      throw new SpeckleException("Input url string was empty");
    }

    if (resources.Length > 1)
    {
      throw new SpeckleException("Input multi-model url is not supported");
    }

    var resource = resources.First();
    using var scope = PriorityLoader.CreateScopeForActiveDocument();
    var urlDerivedAccount = resource.Account.GetAccount(scope);

    // if no account is selected, happily go through the url derived account approach
    if (SelectedAccount == null)
    {
      SetAccount(urlDerivedAccount, false);
    }
    // if we have an account from right-click context-menu, we rely on that and just validate that it's actually applicable to that server
    else if (urlDerivedAccount != null && SelectedAccount.serverInfo.url != urlDerivedAccount.serverInfo.url)
    {
      throw new SpeckleException(
        $"Selected account is for '{SelectedAccount.serverInfo.url}' "
          + $"but URL requires '{urlDerivedAccount.serverInfo.url}'"
      );
    }

    // we have both scenarios covered
    // Scenario #1 - default account from url
    // Scenario #2 - triggered by account switch on right-click context (and validated)
    if (SelectedAccount == null)
    {
      throw new SpeckleException("No account found for the given server url");
    }

    IClient client = _clientFactory.Create(SelectedAccount);

    var project = client.Project.Get(resource.ProjectId).Result;
    var projectPermissions = client.Project.GetPermissions(resource.ProjectId).Result;
    if (project != null && project.workspaceId != null)
    {
      var workspace = client.Workspace.Get(project.workspaceId).Result;
      WorkspaceMenuHandler.RedrawMenuButton(workspace);
    }

    ProjectMenuHandler.RedrawMenuButton(project);

    switch (resource)
    {
      case SpeckleUrlLatestModelVersionResource latestVersionResource:
        var model = client.Model.Get(latestVersionResource.ModelId, latestVersionResource.ProjectId).Result;
        ModelMenuHandler.RedrawMenuButton(model);
        break;
      case SpeckleUrlModelVersionResource versionResource:
        var m = client.Model.Get(versionResource.ModelId, versionResource.ProjectId).Result;
        ModelMenuHandler.RedrawMenuButton(m);

        // TODO: this wont be the case when we have separation between send and receive components
        var v = client.Version.Get(versionResource.VersionId, versionResource.ProjectId).Result;
        VersionMenuHandler?.RedrawMenuButton(v, false);
        break;
      case SpeckleUrlModelObjectResource:
        throw new SpeckleException("Object URLs are not supported");
      default:
        throw new SpeckleException("Unknown Speckle resource type");
    }

    return (resource, isSender ? projectPermissions.canPublish.authorized : projectPermissions.canLoad.authorized);
  }

  public void SetAccount(Account? account, bool refreshComponent = true)
  {
    _updateComponentMessage.Invoke("");
    SelectedAccount = account;

    ResetWorkspaces();
    ResetProjects();
    ResetModels();
    ResetVersions();
    if (refreshComponent)
    {
      _refreshComponent.Invoke();
    }
    if (account != null)
    {
      _accountService.SetUserSelectedAccountId(account.id);
    }
  }

  public void SetAccountFromId(string accountId)
  {
    var account = _accountManager.GetAccount(accountId);
    SetAccount(account, false);
  }

#pragma warning disable CA1054
  public void SetAccountFromIdAndUrl(string accountId, string uri)
#pragma warning restore CA1054
  {
    var account = _accountService.GetAccountWithServerUrlFallback(accountId, new Uri(uri));
    SetAccount(account, false);
  }

  public void SetComponentButtonsState(bool enabled)
  {
    WorkspaceMenuHandler.WorkspaceContextMenuButton.Enabled = enabled;
    ProjectMenuHandler.ProjectContextMenuButton.Enabled = enabled;
    ModelMenuHandler.ModelContextMenuButton.Enabled = enabled;
    if (VersionMenuHandler?.VersionContextMenuButton != null)
    {
      VersionMenuHandler.VersionContextMenuButton.Enabled = enabled;
    }
  }

  public void CreateNewWorkspaceUIState()
  {
    WorkspaceMenuHandler.WorkspaceContextMenuButton.Enabled = true;
    WorkspaceMenuHandler.WorkspaceContextMenuButton.Name = "Create New Workspace";
    ProjectMenuHandler.ProjectContextMenuButton.Enabled = false;
    ModelMenuHandler.ModelContextMenuButton.Enabled = false;
    if (VersionMenuHandler?.VersionContextMenuButton != null)
    {
      VersionMenuHandler.VersionContextMenuButton.Enabled = false;
    }
  }

  public void ResetHandlers()
  {
    WorkspaceMenuHandler.Reset();
    ProjectMenuHandler.Reset();
    ModelMenuHandler.Reset();
    VersionMenuHandler?.Reset();
  }

  public void SetWorkspaceFromSavedIdSync(string workspaceId)
  {
    if (SelectedAccount == null)
    {
      return;
    }
    using IClient client = _clientFactory.Create(SelectedAccount);
    var workspace = client.Workspace.Get(workspaceId).Result;
    SelectedWorkspace = workspace;
    WorkspaceMenuHandler.RedrawMenuButton(SelectedWorkspace);
  }

  public void SetProjectFromSavedIdSync(string projectId)
  {
    if (SelectedAccount == null)
    {
      return;
    }
    using IClient client = _clientFactory.Create(SelectedAccount);
    var project = client.Project.Get(projectId).Result;
    SelectedProject = project;
    ProjectMenuHandler.RedrawMenuButton(SelectedProject);
  }

  public void SetModelFromSavedIdSync(string modelId)
  {
    if (SelectedAccount == null || SelectedProject == null)
    {
      return;
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var model = client.Model.Get(modelId, SelectedProject.id).Result;
    SelectedModel = model;
    ModelMenuHandler.RedrawMenuButton(SelectedModel);
  }

  public void SetVersionFromSavedIdSync(string versionId)
  {
    if (SelectedAccount == null || SelectedProject == null || SelectedModel == null)
    {
      return;
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var version = client.Version.Get(versionId, SelectedProject.id).Result;
    SelectedVersion = version;
    VersionMenuHandler?.RedrawMenuButton(SelectedVersion, IsLatestVersion);
  }

  /// <summary>
  /// Callback function to retrieve workspaces with the search text
  /// </summary>
  public async Task<ResourceCollection<Workspace>> FetchWorkspaces(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<Workspace>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var workspaces = await client.ActiveUser.GetWorkspaces(10, null, new UserWorkspacesFilter(searchText));
    WorkspaceMenuHandler.Workspaces = workspaces;
    return workspaces;
  }

  /// <summary>
  /// Callback function to retrieve workspaces with the search text sync
  /// </summary>
  public ResourceCollection<Workspace> FetchWorkspacesSync(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<Workspace>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var workspaces = client.ActiveUser.GetWorkspaces(10, null, new UserWorkspacesFilter(searchText)).Result;
    WorkspaceMenuHandler.Workspaces = workspaces;
    return workspaces;
  }

  /// <summary>
  /// Callback function to retrieve projects with the search text
  /// </summary>
  public async Task<ResourceCollection<ProjectWithPermissions>> FetchProjects(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<ProjectWithPermissions>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var workspaceId = SelectedWorkspace?.id ?? null;
    var projects = await client.ActiveUser.GetProjectsWithPermissions(
      10,
      null,
      new UserProjectsFilter(
        searchText,
        workspaceId: workspaceId,
        includeImplicitAccess: true,
        personalOnly: WorkspaceMenuHandler.IsPersonalProjects
      )
    );
    ProjectMenuHandler.Projects = projects;
    return projects;
  }

  /// <summary>
  /// Callback function to retrieve projects with the search text sync
  /// </summary>
  public ResourceCollection<ProjectWithPermissions> FetchProjectsSync(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<ProjectWithPermissions>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var workspaceId = SelectedWorkspace?.id ?? null;
    var projects = client
      .ActiveUser.GetProjectsWithPermissions(
        10,
        null,
        new UserProjectsFilter(
          searchText,
          workspaceId: workspaceId,
          includeImplicitAccess: true,
          personalOnly: WorkspaceMenuHandler.IsPersonalProjects
        )
      )
      .Result;
    ProjectMenuHandler.Projects = projects;
    return projects;
  }

  /// <summary>
  /// Callback function to retrieve models with the search text
  /// </summary>
  public async Task<ResourceCollection<Model>> FetchModels(string searchText)
  {
    if (SelectedAccount == null || SelectedProject == null)
    {
      return new ResourceCollection<Model>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var projectWithModels = await client
      .Project.GetWithModels(SelectedProject.id, 10, modelsFilter: new ProjectModelsFilter(search: searchText))
      .ConfigureAwait(true);
    ModelMenuHandler.Models = projectWithModels.models;
    return projectWithModels.models;
  }

  /// <summary>
  /// Callback function to retrieve models with the search text sync
  /// </summary>
  public ResourceCollection<Model> FetchModelsSync(string searchText)
  {
    if (SelectedAccount == null || SelectedProject == null)
    {
      return new ResourceCollection<Model>();
    }

    IClient client = _clientFactory.Create(SelectedAccount);
    var projectWithModels = client
      .Project.GetWithModels(SelectedProject.id, 10, modelsFilter: new ProjectModelsFilter(search: searchText))
      .Result;
    ModelMenuHandler.Models = projectWithModels.models;
    return projectWithModels.models;
  }

  /// <summary>
  /// Callback function to retrieve amount of versions
  /// </summary>
  public async Task<ResourceCollection<Version>> FetchVersions(int versionCount)
  {
    if (SelectedAccount == null || SelectedProject == null || SelectedModel == null)
    {
      return new ResourceCollection<Version>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var newVersionsResult = await client
      .Model.GetWithVersions(SelectedModel.id, SelectedProject.id, versionCount)
      .ConfigureAwait(true);
    if (VersionMenuHandler != null)
    {
      VersionMenuHandler.Versions = newVersionsResult.versions;
    }

    return newVersionsResult.versions;
  }

  /// <summary>
  /// Callback function to retrieve amount of versions
  /// </summary>
  public ResourceCollection<Version> FetchVersionsSync(int versionCount)
  {
    if (SelectedAccount == null || SelectedProject == null || SelectedModel == null)
    {
      return new ResourceCollection<Version>();
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var newVersionsResult = client.Model.GetWithVersions(SelectedModel.id, SelectedProject.id, versionCount).Result;
    if (VersionMenuHandler != null)
    {
      VersionMenuHandler.Versions = newVersionsResult.versions;
    }

    return newVersionsResult.versions;
  }

  public void SetDefaultWorkspaceSync()
  {
    if (SelectedAccount == null)
    {
      return;
    }

    using IClient client = _clientFactory.Create(SelectedAccount);
    var activeWorkspace = client.ActiveUser.GetActiveWorkspace().Result;

    LimitedWorkspace? selectedWorkspace =
      SelectedWorkspace
      ?? activeWorkspace
      ?? (WorkspaceMenuHandler.Workspaces?.items.Count > 0 ? WorkspaceMenuHandler.Workspaces?.items[0] : null);

    SelectedWorkspace = selectedWorkspace;

    WorkspaceMenuHandler.RedrawMenuButton(SelectedWorkspace);
  }

  private void OnWorkspaceSelected(object? sender, WorkspaceSelectedEventArgs e)
  {
    SelectedWorkspace = e.SelectedWorkspace;
    ResetProjects();
    _refreshComponent.Invoke();
  }

  private void OnProjectSelected(object? sender, ProjectSelectedEventArgs e)
  {
    SelectedProject = e.SelectedProject;
    ResetModels();
    _refreshComponent.Invoke();
  }

  private void OnModelSelected(object? sender, ModelSelectedEventArgs e)
  {
    SelectedModel = e.SelectedModel;
    ResetVersions(true);
    _refreshComponent.Invoke();
  }

  private void OnVersionSelected(object? sender, VersionSelectedEventArgs e)
  {
    SelectedVersion = e.SelectedVersion;
    IsLatestVersion = e.IsLatest;
    _refreshComponent.Invoke();
  }

  private void ResetWorkspaces()
  {
    SelectedWorkspace = null;
    WorkspaceMenuHandler.Reset();
    ResetProjects();
  }

  private void ResetProjects()
  {
    SelectedProject = null;
    ProjectMenuHandler.Reset();
    ResetModels();
  }

  private void ResetModels()
  {
    SelectedModel = null;
    ModelMenuHandler.Reset();
    ResetVersions();
  }

  private void ResetVersions(bool defaultToLatest = false)
  {
    SelectedVersion = null;
    VersionMenuHandler?.Reset(defaultToLatest);
  }

  private Task CreateNewWorkspace()
  {
    Process.Start(
      new ProcessStartInfo
      {
        FileName = SelectedAccount?.serverInfo.url + "/workspaces/actions/create",
        UseShellExecute = true
      }
    );
    return Task.CompletedTask;
  }
}
