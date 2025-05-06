using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk.Api;
using Speckle.Sdk.Api.GraphQL.Inputs;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Send;

public class SendWizard
{
  internal Account? SelectedAccount;
  internal readonly IClientFactory ClientFactory;

  public Workspace? SelectedWorkspace { get; internal set; }
  public Project? SelectedProject { get; internal set; }
  public Model? SelectedModel { get; internal set; }

  public WorkspaceMenuHandler WorkspaceMenuHandler { get; }
  public ProjectMenuHandler ProjectMenuHandler { get; }
  public ModelMenuHandler ModelMenuHandler { get; }
  private readonly Func<Task> _refreshComponent;

  public GhContextMenuButton WorkspaceContextMenuButton { get; }
  public GhContextMenuButton ProjectContextMenuButton { get; }
  public GhContextMenuButton ModelContextMenuButton { get; }

  public ResourceCollection<Workspace>? LastFetchedWorkspaces { get; set; }
  public ResourceCollection<Project>? LastFetchedProjects { get; set; }
  public ResourceCollection<Model>? LastFetchedModels { get; set; }

  public SendWizard(Account account, Func<Task> refreshComponent)
  {
    _refreshComponent = refreshComponent;
    SelectedAccount = account;
    ClientFactory = PriorityLoader.Container.GetRequiredService<IClientFactory>();

    WorkspaceMenuHandler = new WorkspaceMenuHandler(FetchWorkspaces);
    WorkspaceContextMenuButton = WorkspaceMenuHandler.WorkspaceContextMenuButton;

    ProjectMenuHandler = new ProjectMenuHandler(FetchProjects); // TODO: Nullability of account need to be handled before
    ProjectContextMenuButton = ProjectMenuHandler.ProjectContextMenuButton;

    ModelMenuHandler = new ModelMenuHandler(FetchModels);
    ModelContextMenuButton = ModelMenuHandler.ModelContextMenuButton;

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

  private void OnWorkspaceSelected(object sender, WorkspaceSelectedEventArgs e)
  {
    SelectedWorkspace = e.SelectedWorkspace;
    SelectedProject = null;
    SelectedModel = null;
    _refreshComponent.Invoke();
  }

  private void OnProjectSelected(object sender, ProjectSelectedEventArgs e)
  {
    SelectedProject = e.SelectedProject;
    SelectedModel = null;
    _refreshComponent.Invoke();
  }

  private void OnModelSelected(object sender, ModelSelectedEventArgs e)
  {
    SelectedModel = e.SelectedModel;
    _refreshComponent.Invoke();
  }
}
