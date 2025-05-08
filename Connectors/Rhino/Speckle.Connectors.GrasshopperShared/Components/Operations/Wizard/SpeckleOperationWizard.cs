using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.GrasshopperShared.Registration;
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
  private Account? _selectedAccount;
  private readonly IClientFactory _clientFactory;

  public Workspace? SelectedWorkspace { get; private set; }
  public Project? SelectedProject { get; private set; }
  public Model? SelectedModel { get; private set; }
  public Version? SelectedVersion { get; private set; }

  public WorkspaceMenuHandler WorkspaceMenuHandler { get; }
  public ProjectMenuHandler ProjectMenuHandler { get; }
  public ModelMenuHandler ModelMenuHandler { get; }
  public VersionMenuHandler? VersionMenuHandler { get; }

  private readonly Func<Task> _refreshComponent;

  /// <param name="account"> Account to get relevant menus for selection.</param>
  /// <param name="refreshComponent"> Callback function to trigger when component need to refresh itself.</param>
  /// <param name="isSender"> Whether it will be used in sender or receiver. Accordingly, the wizard will manage versions or not.</param>
  public SpeckleOperationWizard(Account account, Func<Task> refreshComponent, bool isSender)
  {
    _refreshComponent = refreshComponent;
    _selectedAccount = account;
    _clientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();

    WorkspaceMenuHandler = new WorkspaceMenuHandler(FetchWorkspaces);
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

  public void SetAccount(Account? account)
  {
    _selectedAccount = account;
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

  /// <summary>
  /// Callback function to retrieve workspaces with the search text
  /// </summary>
  private async Task<ResourceCollection<Workspace>> FetchWorkspaces(string searchText)
  {
    if (_selectedAccount == null)
    {
      return new ResourceCollection<Workspace>();
    }

    IClient client = _clientFactory.Create(_selectedAccount);
    var workspaces = await client.ActiveUser.GetWorkspaces(10, null, new UserWorkspacesFilter(searchText));
    return workspaces;
  }

  /// <summary>
  /// Callback function to retrieve projects with the search text
  /// </summary>
  private async Task<ResourceCollection<ProjectWithPermissions>> FetchProjects(string searchText)
  {
    if (_selectedAccount == null)
    {
      return new ResourceCollection<ProjectWithPermissions>();
    }

    IClient client = _clientFactory.Create(_selectedAccount);
    var projects = await client.ActiveUser.GetProjectsWithPermissions(
      10,
      null,
      new UserProjectsFilter(searchText, workspaceId: SelectedWorkspace?.id ?? null, includeImplicitAccess: true)
    );
    return projects;
  }

  /// <summary>
  /// Callback function to retrieve models with the search text
  /// </summary>
  private async Task<ResourceCollection<Model>> FetchModels(string searchText)
  {
    if (_selectedAccount == null || SelectedProject == null)
    {
      return new ResourceCollection<Model>();
    }

    IClient client = _clientFactory.Create(_selectedAccount);
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
    if (_selectedAccount == null || SelectedProject == null || SelectedModel == null)
    {
      return new ResourceCollection<Version>();
    }

    IClient client = _clientFactory.Create(_selectedAccount);
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
