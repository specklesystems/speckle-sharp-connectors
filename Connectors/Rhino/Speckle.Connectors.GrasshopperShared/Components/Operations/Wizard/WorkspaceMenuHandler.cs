using System.Drawing.Drawing2D;
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
  public Bitmap? Logo { get; private set; }
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
    // NOTE: We shouldn't call PopulateMenu here bc it will reset the search item when search is happening, it borks the state.
    PopulateModelMenuItems(_menu!, _searchItem!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    _menu = menu;
    _searchItem = new SearchToolStripMenuItem(menu, Refetch);

    if (Workspaces == null)
    {
      _searchItem.AddMenuItem("No workspaces were fetched");
      return true;
    }

    if (Workspaces.items.Count == 0)
    {
      _searchItem.AddMenuItem("Create a new workspace", (_, _) => CreateNewWorkspace());
      return true;
    }

    PopulateModelMenuItems(_menu, _searchItem);

    return true;
  }

  private void PopulateModelMenuItems(ToolStripDropDown menu, SearchToolStripMenuItem searchItem)
  {
    for (int i = menu.Items.Count - 1; i > 1; i--)
    {
      menu.Items.RemoveAt(i);
    }

    if (Workspaces == null)
    {
      return;
    }

    foreach (var workspace in Workspaces.items)
    {
      searchItem?.AddMenuItem(
        $"{workspace.name}",
        (_, _) => OnWorkspaceSelected(workspace),
        SelectedWorkspace?.id != workspace.id,
        SelectedWorkspace?.id == workspace.id,
        Base64ToImage(workspace.logo)
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
      Logo = Get24X24IconFromBase64(workspace.logo);
      WorkspaceContextMenuButton.SetIconOverride(Logo);
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

  private Image? Base64ToImage(string? base64)
  {
    if (base64 == null)
    {
      return null;
    }
    var base64Data = base64[(base64.IndexOf(',') + 1)..]; // remove data:image/...;base64, part
    byte[] bytes = Convert.FromBase64String(base64Data);
    using var ms = new MemoryStream(bytes);
    return Image.FromStream(ms);
  }

  private Bitmap? Get24X24IconFromBase64(string? base64)
  {
    if (base64 == null)
    {
      return null;
    }
    // Strip metadata prefix
    var base64Data = base64[(base64.IndexOf(',') + 1)..];
    byte[] bytes = Convert.FromBase64String(base64Data);

    using (var ms = new MemoryStream(bytes))
    {
      using (var originalImage = Image.FromStream(ms))
      {
        return ResizeImageToBitmap(originalImage, 24, 24);
      }
    }
  }

  private Bitmap ResizeImageToBitmap(Image image, int width, int height)
  {
    var bmp = new Bitmap(width, height);
    using (var g = Graphics.FromImage(bmp))
    {
      g.CompositingQuality = CompositingQuality.HighQuality;
      g.InterpolationMode = InterpolationMode.HighQualityBicubic;
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.DrawImage(image, 0, 0, width, height);
    }
    return bmp;
  }

  private void CreateNewWorkspace()
  {
    return;
  }
}
