using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;

public class ProjectSelectedEventArgs(ProjectWithPermissions? project) : EventArgs
{
  public ProjectWithPermissions? SelectedProject { get; } = project;
}

/// <summary>
/// Helper class to manage project filtering and selection for the components.
/// </summary>
public class ProjectMenuHandler
{
  private readonly Func<string, Task<ResourceCollection<ProjectWithPermissions>>> _fetchProjects;
  private ToolStripDropDown? _menu;
  private SearchToolStripMenuItem? _searchItem;
  private ProjectWithPermissions? SelectedProject { get; set; }

  public ResourceCollection<ProjectWithPermissions>? Projects { get; set; }

  public event EventHandler<ProjectSelectedEventArgs>? ProjectSelected;

  public GhContextMenuButton ProjectContextMenuButton { get; }

  public ProjectMenuHandler(Func<string, Task<ResourceCollection<ProjectWithPermissions>>> fetchProjects)
  {
    _fetchProjects = fetchProjects;
    ProjectContextMenuButton = new GhContextMenuButton(
      "Select Project",
      "Select Project",
      "Right-click to select project",
      PopulateMenu
    );
  }

  public void Reset()
  {
    _menu?.Items.Clear();
    _menu?.Close();
    SelectedProject = null;
    Projects = null;
    RedrawMenuButton(null);
  }

  public void RedrawMenuButton(Project? project)
  {
    var suffix = ProjectContextMenuButton.Enabled
      ? "Right-click to select another project."
      : "Selection is disabled due to component input.";
    if (project != null)
    {
      ProjectContextMenuButton.Name = project.name;
      ProjectContextMenuButton.NickName = project.id;
      ProjectContextMenuButton.Description = $"{project.description ?? "No description"}\n\n{suffix}";
    }
    else
    {
      ProjectContextMenuButton.Name = "Select Project";
      ProjectContextMenuButton.NickName = "Project";
      ProjectContextMenuButton.Description = "Right-click to select project";
    }
  }

  private async Task Refetch(string searchText)
  {
    Projects = await _fetchProjects.Invoke(searchText);
    // NOTE: We shouldn't call PopulateMenu here bc it will reset the search item when search is happening, it borks the state.
    PopulateMenuItems(_menu!, _searchItem!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    _menu = menu;
    _searchItem = new SearchToolStripMenuItem(menu, Refetch);

    if (Projects == null)
    {
      _searchItem.AddMenuItem("No projects were fetched");
      return true;
    }

    PopulateMenuItems(_menu, _searchItem);
    return true;
  }

  private void PopulateMenuItems(ToolStripDropDown menu, SearchToolStripMenuItem searchItem)
  {
    // Clear previous
    for (int i = menu.Items.Count - 1; i > 1; i--)
    {
      menu.Items.RemoveAt(i);
    }

    if (Projects == null)
    {
      return;
    }

    if (Projects.items.Count == 0 && !string.IsNullOrEmpty(searchItem.SearchText))
    {
      var noProjectsFoundButton = searchItem.AddMenuItem("No projects found.");
      noProjectsFoundButton.BackColor = Color.MistyRose;
      return;
    }

    foreach (var project in Projects.items)
    {
      var desc = string.IsNullOrEmpty(project.description) ? "No description" : project.description;

      var projectItem = searchItem.AddMenuItem(
        $"{project.name} - {desc}",
        (_, _) => OnProjectSelected(project),
        SelectedProject?.id != project.id,
        SelectedProject?.id == project.id
      );
      if (!project.permissions.canLoad.authorized)
      {
        projectItem.Enabled = false;
        projectItem.ToolTipText = @"You do not have permission to do operation on this project.";
      }
    }
  }

  private void OnProjectSelected(ProjectWithPermissions project)
  {
    _menu?.Close();
    SelectedProject = project;
    RedrawMenuButton(project);
    ProjectSelected?.Invoke(this, new ProjectSelectedEventArgs(project));
  }
}
