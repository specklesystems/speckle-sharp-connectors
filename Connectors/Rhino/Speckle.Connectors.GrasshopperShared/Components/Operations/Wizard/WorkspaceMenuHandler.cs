using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;

public class WorkspaceSelectedEventArgs(Workspace? model) : EventArgs
{
  public Workspace? SelectedWorkspace { get; } = model;
}

public class WorkspaceMenuHandler
{
  private readonly Func<string, Task<ResourceCollection<Workspace>>> _fetchWorkspaces;
  private ToolStripDropDown? _menu;
  private SearchToolStripMenuItem? _searchItem;
  private Workspace? SelectedWorkspace { get; set; }

  public ResourceCollection<Workspace>? Workspaces { get; set; }

  public event EventHandler<WorkspaceSelectedEventArgs>? WorkspaceSelected;

  public GhContextMenuButton WorkspaceContextMenuButton { get; }

  public WorkspaceMenuHandler(Func<string, Task<ResourceCollection<Workspace>>> fetchWorkspaces)
  {
    _fetchWorkspaces = fetchWorkspaces;
    WorkspaceContextMenuButton = new GhContextMenuButton(
      "Select Workspace",
      "Select Workspace",
      "Right-click to select a workspace",
      PopulateMenu
    );
  }

  public void Reset()
  {
    _menu?.Close();
    RedrawMenuButton(null);
  }

  private async Task Refetch(string searchText)
  {
    Workspaces = await _fetchWorkspaces.Invoke(searchText);
    PopulateMenu(_menu!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    _menu = menu;
    _menu.Closed += (_, _) =>
    {
      _searchItem = null;
    };
    _searchItem ??= new SearchToolStripMenuItem(menu, Refetch);

    if (Workspaces == null)
    {
      _searchItem?.AddMenuItem("No workspaces were fetched");
      return true;
    }

    if (Workspaces.items.Count == 0)
    {
      _searchItem?.AddMenuItem("Create a new workspace", (_, _) => CreateNewWorkspace());
      return true;
    }

    PopulateModelMenuItems(menu);

    return true;
  }

  private void PopulateModelMenuItems(ToolStripDropDown menu)
  {
    var lastIndex = menu.Items.Count - 1;
    if (lastIndex >= 0)
    {
      // clean the existing items because we re-populate when user search
      for (int i = lastIndex; i > 1; i--)
      {
        menu.Items.RemoveAt(i);
      }
    }

    if (Workspaces == null)
    {
      return;
    }

    _searchItem ??= new SearchToolStripMenuItem(menu, Refetch);

    foreach (var workspace in Workspaces.items)
    {
      var desc = string.IsNullOrEmpty(workspace.description) ? "No description" : workspace.description;

      _searchItem?.AddMenuItem(
        $"{workspace.name}",
        (_, _) => OnWorkspaceSelected(workspace),
        SelectedWorkspace?.id != workspace.id,
        SelectedWorkspace?.id == workspace.id
      );
    }
  }

  private void OnWorkspaceSelected(Workspace workspace)
  {
    _menu?.Close();
    SelectedWorkspace = workspace;
    RedrawMenuButton(workspace);
    WorkspaceSelected?.Invoke(this, new WorkspaceSelectedEventArgs(workspace));
  }

  public void RedrawMenuButton(Workspace? workspace)
  {
    var suffix = WorkspaceContextMenuButton.Enabled
      ? "Right-click to select another workspace."
      : "Selection is disabled due to component input.";
    if (workspace != null)
    {
      WorkspaceContextMenuButton.Name = workspace.name;
      WorkspaceContextMenuButton.NickName = workspace.id;
      WorkspaceContextMenuButton.Description = $"{workspace.description ?? "No description"}\n\n{suffix}";
    }
    else
    {
      WorkspaceContextMenuButton.Name = "Select Workspace";
      WorkspaceContextMenuButton.NickName = "Workspace";
      WorkspaceContextMenuButton.Description = "Right-click to select workspace";
    }
  }

  private void CreateNewWorkspace()
  {
    return;
  }
}
