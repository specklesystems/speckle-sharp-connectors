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
  public List<Account> Accounts { get; private set; }
  public Workspace? SelectedWorkspace { get; private set; }
  public Project? SelectedProject { get; private set; }
  public Model? SelectedModel { get; private set; }
  public Version? SelectedVersion { get; private set; }

  public WorkspaceMenuHandler WorkspaceMenuHandler { get; }
  public ProjectMenuHandler ProjectMenuHandler { get; }
  public ModelMenuHandler ModelMenuHandler { get; }
  public VersionMenuHandler? VersionMenuHandler { get; }

  private readonly Func<Task> _refreshComponent;
  private readonly AccountService _accountService;
  private readonly AccountManager _accountManager;

  /// <param name="refreshComponent"> Callback function to trigger when component need to refresh itself.</param>
  /// <param name="isSender"> Whether it will be used in sender or receiver. Accordingly, the wizard will manage versions or not.</param>
  public SpeckleOperationWizard(Func<Task> refreshComponent, bool isSender)
  {
    _refreshComponent = refreshComponent;
    _clientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();
    _accountManager = PriorityLoader.Container.GetRequiredService<AccountManager>();
    _accountService = PriorityLoader.Container.GetRequiredService<AccountService>();

    var userSelectedAccountId = _accountService.GetUserSelectedAccountId();
    SelectedAccount =
      userSelectedAccountId != null
        ? _accountManager.GetAccount(userSelectedAccountId)
        : _accountManager.GetDefaultAccount();

    WorkspaceMenuHandler = new WorkspaceMenuHandler(FetchWorkspaces, CreateNewWorkspace);
    ProjectMenuHandler = new ProjectMenuHandler(FetchProjects); // TODO: Nullability of account need to be handled before
    ModelMenuHandler = new ModelMenuHandler(FetchModels);
    if (!isSender)
    {
      VersionMenuHandler = new VersionMenuHandler(FetchMoreVersions);
      VersionMenuHandler.VersionSelected += OnVersionSelected;
    }

    WorkspaceMenuHandler.WorkspaceSelected += OnWorkspaceSelected;
    ProjectMenuHandler.ProjectSelected += OnProjectSelected;
    ModelMenuHandler.ModelSelected += OnModelSelected;
  }

  public (SpeckleUrlModelResource resource, string accountId) SolveInstanceWithUrlInput(string input)
  {
    // When input is provided, lock interaction of buttons so only text is shown (no context menu)
    // Should perform validation, fill in all internal data of the component (project, model, version, account)
    // Should notify user if any of this goes wrong.

    var resources = SpeckleResourceBuilder.FromUrlString(input);
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
    SetAccount(account);

    if (SelectedAccount == null)
    {
      throw new SpeckleException("No account found for server URL");
    }

    IClient client = _clientFactory.Create(SelectedAccount);

    var project = client.Project.Get(resource.ProjectId).Result;
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
        VersionMenuHandler?.RedrawMenuButton(v);
        break;
      case SpeckleUrlModelObjectResource:
        throw new SpeckleException("Object URLs are not supported");
      default:
        throw new SpeckleException("Unknown Speckle resource type");
    }

    return (resource, account.id);
  }

  public void SetAccount(Account? account)
  {
    SelectedAccount = account;
    ResetWorkspaces();
    ResetProjects();
    ResetModels();
    ResetVersions();
  }

  public void SetDefaultWorkspace(Workspace workspace)
  {
    SelectedWorkspace = workspace;
    WorkspaceMenuHandler.RedrawMenuButton(SelectedWorkspace);
  }

  public void SetWorkspaces(ResourceCollection<Workspace> workspaces)
  {
    WorkspaceMenuHandler.Workspaces = workspaces;
  }

  public void SetProjects(ResourceCollection<ProjectWithPermissions>? projects)
  {
    ProjectMenuHandler.Projects = projects;
  }

  public void SetModels(ResourceCollection<Model> models)
  {
    ModelMenuHandler.Models = models;
  }

  public void SetVersions(ResourceCollection<Version> versions)
  {
    if (VersionMenuHandler != null)
    {
      VersionMenuHandler.Versions = versions;
    }
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

  private void ResetVersions()
  {
    SelectedVersion = null;
    VersionMenuHandler?.Reset();
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

  /// <summary>
  /// Callback function to retrieve workspaces with the search text
  /// </summary>
  private async Task<ResourceCollection<Workspace>> FetchWorkspaces(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<Workspace>();
    }

    IClient client = _clientFactory.Create(SelectedAccount);
    var workspaces = await client.ActiveUser.GetWorkspaces(10, null, new UserWorkspacesFilter(searchText));
    return workspaces;
  }

  /// <summary>
  /// Callback function to retrieve projects with the search text
  /// </summary>
  private async Task<ResourceCollection<ProjectWithPermissions>> FetchProjects(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<ProjectWithPermissions>();
    }

    IClient client = _clientFactory.Create(SelectedAccount);
    var workspaceId = SelectedWorkspace?.id ?? null;
    var projects = await client.ActiveUser.GetProjectsWithPermissions(
      10,
      null,
      new UserProjectsFilter(searchText, workspaceId: workspaceId, includeImplicitAccess: true)
    );
    return projects;
  }

  /// <summary>
  /// Callback function to retrieve models with the search text
  /// </summary>
  private async Task<ResourceCollection<Model>> FetchModels(string searchText)
  {
    if (SelectedAccount == null || SelectedProject == null)
    {
      return new ResourceCollection<Model>();
    }

    IClient client = _clientFactory.Create(SelectedAccount);
    var projectWithModels = await client
      .Project.GetWithModels(SelectedProject.id, 10, modelsFilter: new ProjectModelsFilter(search: searchText))
      .ConfigureAwait(true);
    return projectWithModels.models;
  }

  /// <summary>
  /// Callback function to retrieve amount of versions
  /// </summary>
  private async Task<ResourceCollection<Version>> FetchMoreVersions(int versionCount)
  {
    if (SelectedAccount == null || SelectedProject == null || SelectedModel == null)
    {
      return new ResourceCollection<Version>();
    }

    IClient client = _clientFactory.Create(SelectedAccount);
    var newVersionsResult = await client
      .Model.GetWithVersions(SelectedModel.id, SelectedProject.id, versionCount)
      .ConfigureAwait(true);
    return newVersionsResult.versions;
  }

  private void OnWorkspaceSelected(object sender, WorkspaceSelectedEventArgs e)
  {
    SelectedWorkspace = e.SelectedWorkspace;
    ResetProjects();
    _refreshComponent.Invoke();
  }

  private void OnProjectSelected(object sender, ProjectSelectedEventArgs e)
  {
    SelectedProject = e.SelectedProject;
    ResetModels();
    _refreshComponent.Invoke();
  }

  private void OnModelSelected(object sender, ModelSelectedEventArgs e)
  {
    SelectedModel = e.SelectedModel;
    ResetVersions();
    _refreshComponent.Invoke();
  }

  private void OnVersionSelected(object sender, VersionSelectedEventArgs e)
  {
    SelectedVersion = e.SelectedVersion;
    _refreshComponent.Invoke();
  }
}
