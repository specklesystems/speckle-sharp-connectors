using Speckle.Sdk.Api.GraphQL.Models;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations.Wizard;

public class VersionSelectedEventArgs(Version? version, bool isLatest) : EventArgs
{
  public Version? SelectedVersion { get; } = version;
  public bool IsLatest { get; } = isLatest;
}

public class VersionMenuHandler
{
  private int FetchedVersionCount { get; set; } = 10;
  private readonly Func<int, Task<ResourceCollection<Version>>> _fetchVersions;
  private ToolStripDropDown? _menu;
  private Version? SelectedVersion { get; set; }
  public bool IsLatest { get; set; }

  public ResourceCollection<Version>? Versions { get; set; }

  public event EventHandler<VersionSelectedEventArgs>? VersionSelected;

  public GhContextMenuButton VersionContextMenuButton { get; }

  public VersionMenuHandler(Func<int, Task<ResourceCollection<Version>>> fetchVersions)
  {
    _fetchVersions = fetchVersions;
    VersionContextMenuButton = new GhContextMenuButton(
      "Select Version",
      "Select Project",
      "Left-click to select a version",
      PopulateMenu
    );
  }

  public void Reset(bool isLatest = false)
  {
    _menu?.Items.Clear();
    _menu?.Close();
    IsLatest = isLatest;
    SelectedVersion = null;
    Versions = null;
    FetchedVersionCount = 10;
    RedrawMenuButton(null, isLatest);
  }

  private async Task Refetch(int versionCount)
  {
    Versions = await _fetchVersions.Invoke(versionCount);
    PopulateVersionMenuItems(_menu!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
    menu.LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow;
    _menu = menu;

    if (Versions is null)
    {
      AddMenuItem("No versions were fetched");
      return true;
    }

    if (Versions.items.Count == 0)
    {
      AddMenuItem("Model has no versions");
      return true;
    }

    PopulateVersionMenuItems(menu);

    return true;
  }

  private void PopulateVersionMenuItems(ToolStripDropDown menu)
  {
    menu.Items.Clear();

    if (Versions == null)
    {
      return;
    }

    AddMenuItem("Latest Version", (_, _) => OnVersionSelected(null, true), true, SelectedVersion == null);
    AddMenuSeparator();

    foreach (var version in Versions.items)
    {
      var desc = string.IsNullOrEmpty(version.message) ? "No description" : version.message;

      var versionItem = AddMenuItem(
        $"{version.id} - {desc}",
        (_, _) => OnVersionSelected(version, false),
        true,
        SelectedVersion?.id == version.id
      );
      if (version.referencedObject is null)
      {
        versionItem.Enabled = false;
        versionItem.ToolTipText = @"Upgrade to load older versions";
      }
    }

    if (Versions.items.Count >= FetchedVersionCount)
    {
      AddMenuSeparator();

      var addMoreButton = new Button() { Text = @"➕ Show more...", Size = new Size(200, 32) };

      addMoreButton.Click += async (_, _) =>
      {
        FetchedVersionCount += 10;
        await Refetch(FetchedVersionCount);
      };

      var addMoreButtonHost = new ToolStripControlHost(addMoreButton)
      {
        Name = "Show more...",
        AutoSize = false,
        Margin = new Padding(4),
        Padding = new Padding(2),
      };

      menu.Items.Insert(menu.Items.Count, addMoreButtonHost);
    }
  }

  public void RedrawMenuButton(Version? version, bool isLatest)
  {
    IsLatest = isLatest;
    var suffix = VersionContextMenuButton.Enabled
      ? "Left-click to select another version."
      : "Selection is disabled due to component input.";
    if (version != null)
    {
      VersionContextMenuButton.Name = version.id;
      VersionContextMenuButton.NickName = version.id;
      VersionContextMenuButton.Description = $"{version.message ?? "No message"}\n\n{suffix}";
    }
    else
    {
      VersionContextMenuButton.Name = IsLatest ? "Latest Version" : "Select Version";
      VersionContextMenuButton.NickName = "Version";
      VersionContextMenuButton.Description = "Left-click to select a specific version";
    }
  }

  private void OnVersionSelected(Version? version, bool isLatest)
  {
    _menu?.Close();
    SelectedVersion = version;
    IsLatest = isLatest;
    RedrawMenuButton(SelectedVersion, isLatest);
    VersionSelected?.Invoke(this, new VersionSelectedEventArgs(version, isLatest));
  }

  private ToolStripMenuItem AddMenuItem(
    string text,
    EventHandler? click = null,
    bool? visible = null,
    bool? isChecked = null
  )
  {
    var item = new ToolStripMenuItem(text) { Checked = isChecked ?? false, TextAlign = ContentAlignment.MiddleLeft, };
    item.Click += click;
    if (visible == false)
    {
      item.Visible = false;
    }

    _menu?.Items.Add(item);
    return item;
  }

  private void AddMenuSeparator()
  {
    _menu?.Items.Add(new ToolStripSeparator());
  }
}
