using System.Drawing.Drawing2D;
using Speckle.Sdk;
using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;

public class WorkspaceSelectedEventArgs(LimitedWorkspace? model) : EventArgs
{
  public LimitedWorkspace? SelectedWorkspace { get; } = model;
}

public class WorkspaceMenuHandler
{
  private readonly Func<string, Task<ResourceCollection<Workspace>?>> _fetchWorkspaces;
  private ToolStripDropDown? _menu;
  public bool IsPersonalProjects { get; private set; }
  private SearchToolStripMenuItem? _searchItem;
  private readonly Func<Task> _createWorkspace;
  private LimitedWorkspace? SelectedWorkspace { get; set; }

  public ResourceCollection<Workspace>? Workspaces { get; set; }
  public Bitmap? Logo { get; private set; }
  public event EventHandler<WorkspaceSelectedEventArgs>? WorkspaceSelected;

  public GhContextMenuButton WorkspaceContextMenuButton { get; }

  public WorkspaceMenuHandler(
    Func<string, Task<ResourceCollection<Workspace>?>> fetchWorkspaces,
    Func<Task> createWorkspace
  )
  {
    _fetchWorkspaces = fetchWorkspaces;
    _createWorkspace = createWorkspace;
    WorkspaceContextMenuButton = new GhContextMenuButton(
      "Select Workspace",
      "Select Workspace",
      "Left-click to select a workspace",
      PopulateMenu
    );
  }

  public void Reset()
  {
    _menu?.Items.Clear();
    _menu?.Close();
    Workspaces = null;
    SelectedWorkspace = null;
    IsPersonalProjects = false;
    RedrawMenuButton(null);
  }

  private async Task Refetch(string searchText)
  {
    Workspaces = await _fetchWorkspaces.Invoke(searchText);
    // NOTE: We shouldn't call PopulateMenu here bc it will reset the search item when search is happening, it borks the state.
    PopulateModelMenuItems(_menu!, _searchItem);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    menu.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
    _menu = menu;
    _searchItem = new SearchToolStripMenuItem(menu, Refetch);

    if (Workspaces?.items.Count == 0)
    {
      menu.Items.Clear();
      _searchItem.AddMenuItem("Create a new workspace", (_, _) => _createWorkspace.Invoke());
      return true;
    }

    PopulateModelMenuItems(_menu, _searchItem);

    return true;
  }

  private void PopulateModelMenuItems(ToolStripDropDown menu, SearchToolStripMenuItem? searchItem)
  {
    for (int i = menu.Items.Count - 1; i > 1; i--)
    {
      menu.Items.RemoveAt(i);
    }

    if (Workspaces == null)
    {
      searchItem?.AddMenuItem(
        "Personal Projects",
        (_, _) => OnWorkspaceSelected(null),
        !IsPersonalProjects,
        IsPersonalProjects
      );
      return;
    }

    foreach (var workspace in Workspaces.items)
    {
      searchItem?.AddMenuItem(
        $"{workspace.name}",
        (_, _) => OnWorkspaceSelected(workspace),
        SelectedWorkspace?.id != workspace.id,
        SelectedWorkspace?.id == workspace.id,
        Base64ToImage(workspace.logoUri)
      );
    }
  }

  private void OnWorkspaceSelected(LimitedWorkspace? workspace)
  {
    IsPersonalProjects = workspace == null;
    _menu?.Close();
    SelectedWorkspace = workspace;
    RedrawMenuButton(workspace);
    WorkspaceSelected?.Invoke(this, new WorkspaceSelectedEventArgs(workspace));
  }

  public void RedrawMenuButton(LimitedWorkspace? workspace)
  {
    var suffix = WorkspaceContextMenuButton.Enabled
      ? "Left-click to select another workspace."
      : "Selection is disabled due to component input.";
    if (workspace != null && !IsPersonalProjects)
    {
      Logo = Get24X24IconFromBase64(workspace.logoUri);
      WorkspaceContextMenuButton.SetIconOverride(Logo);
      WorkspaceContextMenuButton.Name = workspace.name;
      WorkspaceContextMenuButton.NickName = workspace.id;
      WorkspaceContextMenuButton.Description = $"{workspace.description ?? "No description"}\n\n{suffix}";
    }
    else if (IsPersonalProjects)
    {
      WorkspaceContextMenuButton.SetIconOverride(null);
      WorkspaceContextMenuButton.Name = "Personal Projects";
      WorkspaceContextMenuButton.NickName = "Personal Projects";
      WorkspaceContextMenuButton.Description = "Legacy"; //Servers that don't have workspaces enabled (e.g. public opensource server (v2.x))
    }
    else
    {
      WorkspaceContextMenuButton.SetIconOverride(null);
      WorkspaceContextMenuButton.Name = "Select Workspace";
      WorkspaceContextMenuButton.NickName = "Workspace";
      WorkspaceContextMenuButton.Description = "Left-click to select workspace";
    }
  }

  private Image? Base64ToImage(string? logoUri)
  {
    if (!TryDecodeLogo(logoUri, out byte[] bytes))
    {
      return null;
    }
    using var ms = new MemoryStream(bytes);
    return Image.FromStream(ms);
  }

  private Bitmap? Get24X24IconFromBase64(string? logoUri)
  {
    if (!TryDecodeLogo(logoUri, out byte[] bytes))
    {
      return null;
    }

    using (var ms = new MemoryStream(bytes))
    {
      using (var originalImage = Image.FromStream(ms))
      {
        return ResizeImageToBitmap(originalImage, 24, 24);
      }
    }
  }

  // workspace.logoUri is a base64 data URI (data:image/...;base64,XXXX); decode it, tolerating a bare base64 string
  // too. Returns false (rather than throwing) for null/empty or a remote http(s) URL, so a server-side switch to a
  // hosted logo URL degrades to "no icon" instead of crashing the menu redraw.
  private static bool TryDecodeLogo(string? logoUri, out byte[] bytes)
  {
    bytes = Array.Empty<byte>();
    if (string.IsNullOrEmpty(logoUri) || logoUri!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }
    try
    {
      var base64Data = logoUri[(logoUri.IndexOf(',') + 1)..]; // strip any data:image/...;base64, prefix
      bytes = Convert.FromBase64String(base64Data);
      return true;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return false;
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
}
