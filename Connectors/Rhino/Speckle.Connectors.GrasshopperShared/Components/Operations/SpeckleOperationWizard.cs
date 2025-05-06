using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public class SpeckleOperationWizard
{
  internal Account? SelectedAccount;
  internal readonly IClientFactory ClientFactory;

  public Workspace? SelectedWorkspace { get; private set; }
  public Project? SelectedProject { get; private set; }
  public Model? SelectedModel { get; private set; }
  public Version? SelectedVersion { get; private set; }

  public WorkspaceMenuHandler WorkspaceMenuHandler { get; }
  public ProjectMenuHandler ProjectMenuHandler { get; }
  public ModelMenuHandler ModelMenuHandler { get; }
  public VersionMenuHandler? VersionMenuHandler { get; }

  internal readonly Func<Task> RefreshComponent;

  public ResourceCollection<Workspace>? LastFetchedWorkspaces { get; set; }
  public ResourceCollection<Project>? LastFetchedProjects { get; set; }
  public ResourceCollection<Model>? LastFetchedModels { get; set; }
  public ResourceCollection<Version>? LastFetchedVersions { get; set; }

  public SpeckleOperationWizard(Account account, Func<Task> refreshComponent, bool isSender)
  {
    RefreshComponent = refreshComponent;
    SelectedAccount = account;
    ClientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();

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

  public void SetAccount(Account account)
  {
    SelectedAccount = account;
    SelectedWorkspace = null;
    SelectedProject = null;
    SelectedModel = null;
    LastFetchedWorkspaces = null;
    LastFetchedProjects = null;
    LastFetchedModels = null;
  }

  public void SetWorkspaces(ResourceCollection<Workspace> workspaces)
  {
    LastFetchedWorkspaces = workspaces;
    WorkspaceMenuHandler.Workspaces = workspaces;
  }

  public void SetProjects(ResourceCollection<Project>? projects)
  {
    LastFetchedProjects = projects;
    ProjectMenuHandler.Projects = projects;
  }

  public void SetModels(ResourceCollection<Model> models)
  {
    LastFetchedModels = models;
    ModelMenuHandler.Models = models;
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

    IClient client = ClientFactory.Create(SelectedAccount);
    var workspaces = await client.ActiveUser.GetWorkspaces(10, null, new UserWorkspacesFilter(searchText));
    LastFetchedWorkspaces = workspaces;
    return workspaces;
  }

  /// <summary>
  /// Callback function to retrieve projects with the search text
  /// </summary>
  private async Task<ResourceCollection<Project>> FetchProjects(string searchText)
  {
    if (SelectedAccount == null)
    {
      return new ResourceCollection<Project>();
    }

    IClient client = ClientFactory.Create(SelectedAccount);
    var projects = await client.ActiveUser.GetProjects(10, null, new UserProjectsFilter(searchText));
    LastFetchedProjects = projects;
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

    IClient client = ClientFactory.Create(SelectedAccount);
    var projectWithModels = await client
      .Project.GetWithModels(SelectedProject.id, 10, modelsFilter: new ProjectModelsFilter(search: searchText))
      .ConfigureAwait(true);
    LastFetchedModels = projectWithModels.models;
    return projectWithModels.models;
  }

  public void SetVersions(ResourceCollection<Version> versions)
  {
    if (VersionMenuHandler != null)
    {
      LastFetchedVersions = versions;
      VersionMenuHandler.Versions = versions;
    }
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

    IClient client = ClientFactory.Create(SelectedAccount);
    var newVersionsResult = await client
      .Model.GetWithVersions(SelectedModel.id, SelectedProject.id, versionCount)
      .ConfigureAwait(true);
    LastFetchedVersions = newVersionsResult.versions;
    return newVersionsResult.versions;
  }

  private void OnWorkspaceSelected(object sender, WorkspaceSelectedEventArgs e)
  {
    SelectedWorkspace = e.SelectedWorkspace;

    SelectedProject = null;
    SelectedModel = null;

    ProjectMenuHandler.Reset();
    ModelMenuHandler.Reset();
    VersionMenuHandler?.Reset();
    RefreshComponent.Invoke();
  }

  private void OnProjectSelected(object sender, ProjectSelectedEventArgs e)
  {
    SelectedProject = e.SelectedProject;

    SelectedModel = null;
    ModelMenuHandler.Reset();
    VersionMenuHandler?.Reset();

    RefreshComponent.Invoke();
  }

  private void OnModelSelected(object sender, ModelSelectedEventArgs e)
  {
    SelectedModel = e.SelectedModel;

    VersionMenuHandler?.Reset();

    RefreshComponent.Invoke();
  }

  private void OnVersionSelected(object sender, VersionSelectedEventArgs e)
  {
    SelectedVersion = e.SelectedVersion;

    RefreshComponent.Invoke();
  }
}
