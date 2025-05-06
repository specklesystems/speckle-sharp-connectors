using Speckle.Sdk.Api.GraphQL.Models;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Connectors.GrasshopperShared.Components.Operations;

public class VersionSelectedEventArgs(Version? version) : EventArgs
{
  public Version? SelectedVersion { get; } = version;
}

public class VersionMenuHandler
{
  private int FetchedVersionCount { get; set; } = 10;
  private readonly Func<int, Task<ResourceCollection<Version>>> _fetchVersions;
  private ToolStripDropDown? _menu;
  private Version? SelectedVersion { get; set; }

  public ResourceCollection<Version>? Versions { get; set; }

  public event EventHandler<VersionSelectedEventArgs>? VersionSelected;

  public GhContextMenuButton VersionContextMenuButton { get; set; }

  public VersionMenuHandler(Func<int, Task<ResourceCollection<Version>>> fetchVersions)
  {
    _fetchVersions = fetchVersions;
    VersionContextMenuButton = new GhContextMenuButton(
      "Select Version",
      "Select Project",
      "Right-click to select a version",
      PopulateMenu
    );
  }

  public void Reset()
  {
    FetchedVersionCount = 10;
    RedrawMenuButton(null);
  }

  private async Task Refetch(int versionCount)
  {
    Versions = await _fetchVersions.Invoke(versionCount);
    PopulateMenu(_menu!);
  }

  private bool PopulateMenu(ToolStripDropDown menu)
  {
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

    AddMenuItem("Latest Version", (_, _) => OnVersionSelected(null), true, SelectedVersion == null);
    AddMenuSeparator();

    foreach (var version in Versions.items)
    {
      var desc = string.IsNullOrEmpty(version.message) ? "No description" : version.message;

      var versionItem = AddMenuItem(
        $"{version.id} - {desc}",
        (_, _) => OnVersionSelected(version),
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
        Padding = new Padding(2)
      };

      menu.Items.Insert(menu.Items.Count, addMoreButtonHost);
    }
  }

  public void RedrawMenuButton(Version? version)
  {
    var suffix = VersionContextMenuButton.Enabled
      ? "Right-click to select another version."
      : "Selection is disabled due to component input.";
    if (version != null)
    {
      VersionContextMenuButton.Name = version.id;
      VersionContextMenuButton.NickName = version.id;
      VersionContextMenuButton.Description = $"{version.message ?? "No message"}\n\n{suffix}";
    }
    // else if (_model != null)
    // {
    //   VersionContextMenuButton.NickName = "Latest Version";
    //   VersionContextMenuButton.Name = "Latest Version";
    //   VersionContextMenuButton.Description = "Gets the latest version from the selected model";
    // }
    else
    {
      VersionContextMenuButton.Name = "Select Version";
      VersionContextMenuButton.NickName = "Version";
      VersionContextMenuButton.Description = "Right-click to select version";
    }
  }

  private void OnVersionSelected(Version? version)
  {
    _menu?.Close();
    SelectedVersion = version;
    RedrawMenuButton(SelectedVersion);
    VersionSelected?.Invoke(this, new VersionSelectedEventArgs(version));
  }

  private ToolStripMenuItem AddMenuItem(
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

    _menu?.Items.Add(item);
    return item;
  }

  private void AddMenuSeparator() => _menu?.Items.Add(new ToolStripSeparator());
}
