using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class ProjectSelectedEventArgs(Project? project) : EventArgs
{
  public Project? SelectedProject { get; } = project;
}

/// <summary>
/// Helper class to manage project filtering and selection for the components.
/// </summary>
public class ProjectMenuHandler
{
  private readonly Func<string, Task<ResourceCollection<Project>>> _fetchProjects;
  private ToolStripDropDown? _menu;
  private SearchToolStripMenuItem? _searchItem;
  private Project? SelectedProject { get; set; }

  public ResourceCollection<Project>? Projects { get; set; }

  public event EventHandler<ProjectSelectedEventArgs>? ProjectSelected;

  public GhContextMenuButton ProjectContextMenuButton { get; set; }

  public ProjectMenuHandler(Func<string, Task<ResourceCollection<Project>>> fetchProjects)
  {
    _fetchProjects = fetchProjects;
    ProjectContextMenuButton = new GhContextMenuButton(
      "Select Project",
      "Select Project",
      "Right-click to select project",
      PopulateMenu
    );
  }

  private async Task Refetch(string searchText)
  {
    Projects = await _fetchProjects.Invoke(searchText);
    PopulateMenuItems(_menu!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    _menu = menu;
    menu.Closed += (_, _) =>
    {
      _searchItem = null;
    };

    if (Projects == null)
    {
      _searchItem?.AddMenuItem("No projects were fetched");
      return true;
    }

    PopulateMenuItems(menu);
    return true;
  }

  private void PopulateMenuItems(ToolStripDropDown menu)
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

    if (_searchItem == null)
    {
      _searchItem = new SearchToolStripMenuItem(menu, Refetch);
      Menu_AppendSeparator(menu);
    }

    if (Projects.items.Count == 0 && !string.IsNullOrEmpty(_searchItem.SearchText))
    {
      var noProjectsFoundButton = Menu_AppendItem(menu, "No projects found.");
      noProjectsFoundButton.BackColor = Color.MistyRose;
      return;
    }

    foreach (var project in Projects.items)
    {
      var desc = string.IsNullOrEmpty(project.description) ? "No description" : project.description;

      Menu_AppendItem(
        menu,
        $"{project.name} - {desc}",
        (_, _) => OnProjectSelected(project),
        SelectedProject?.id != project.id,
        SelectedProject?.id == project.id
      );
    }
  }

  private void OnProjectSelected(Project project)
  {
    _menu?.Close();
    SelectedProject = project;
    var suffix = ProjectContextMenuButton.Enabled
      ? "Right-click to select another project."
      : "Selection is disabled due to component input.";
    if (SelectedProject != null)
    {
      ProjectContextMenuButton.Name = SelectedProject.name;
      ProjectContextMenuButton.NickName = SelectedProject.id;
      ProjectContextMenuButton.Description = $"{SelectedProject.description ?? "No description"}\n\n{suffix}";
    }
    else
    {
      ProjectContextMenuButton.Name = "Select Project";
      ProjectContextMenuButton.NickName = "Project";
      ProjectContextMenuButton.Description = "Right-click to select project";
    }
    ProjectSelected?.Invoke(this, new ProjectSelectedEventArgs(project));
  }

  private static ToolStripMenuItem Menu_AppendItem(
    ToolStripDropDown menu,
    string text,
    EventHandler? click = null,
    bool? visible = null,
    bool? isChecked = null
  )
  {
    var item = new ToolStripMenuItem(text) { Checked = isChecked ?? false };
    item.Click += click;
    if (visible == false)
    {
      item.Visible = false;
    }

    menu.Items.Add(item);
    return item;
  }

  private static void Menu_AppendSeparator(ToolStripDropDown menu) => menu.Items.Add(new ToolStripSeparator());
}
