using System.Windows.Forms;
using Speckle.Connector.Navisworks.Plugin.Tools;

namespace Speckle.Connector.Navisworks.Plugin;

/// <summary>
/// Handles plugin state and ribbon management for the Speckle V3 and V2 connectors.
/// </summary>
[
  NAV.Plugins.Plugin(SpeckleV3Tool.PLUGIN_ID, SpeckleV3Tool.DEVELOPER_ID, DisplayName = SpeckleV3Tool.DISPLAY_NAME),
  NAV.Plugins.Strings(SpeckleV3Tool.RIBBON_STRINGS),
  NAV.Plugins.RibbonLayout("NavisworksRibbon.xaml"),
  NAV.Plugins.RibbonTab(
    SpeckleV3Tool.RIBBON_TAB_ID,
    DisplayName = SpeckleV3Tool.RIBBON_TAB_DISPLAY_NAME,
    LoadForCanExecute = true
  ),
  NAV.Plugins.Command(
    SpeckleV3Tool.COMMAND,
    LoadForCanExecute = true,
    Icon = "Resources/v3_logo16.png",
    LargeIcon = "Resources/v3_logo32.png",
    ToolTip = "Speckle Connector for Navisworks",
    DisplayName = "$Speckle_Launch.DisplayName"
  ),
  NAV.Plugins.Command(
    SpeckleV2Tool.COMMAND,
    LoadForCanExecute = true,
    Icon = "Resources/v2_logo16.png",
    LargeIcon = "Resources/v2_logo32.png",
    ToolTip = "Legacy Speckle v2 Connector",
    DisplayName = "$Speckle_Launch_V2.DisplayName"
  )
]
internal sealed class RibbonHandler : NAV.Plugins.CommandHandlerPlugin
{
  private static bool? s_isV2PluginAvailable; // Nullable to indicate uncached state.
  private static bool s_isV2RibbonHidden; // Tracks if the ribbon tab is already hidden.

  static RibbonHandler()
  {
    // Subscribe to the static PluginRecordsChanged event
    NAV.ApplicationParts.ApplicationPlugins.PluginRecordsChanged += OnPluginRecordsChanged;
  }

  private static void OnPluginRecordsChanged(object sender, EventArgs e) => s_isV2PluginAvailable = null;

  /// <summary>
  /// Determines whether a command can be executed and manages V2 plugin visibility.
  /// </summary>
  /// <param name="commandId">The command identifier to check.</param>
  /// <returns>A CommandState indicating whether the command can be executed.</returns>
  public override NAV.Plugins.CommandState CanExecuteCommand(string commandId)
  {
    switch (commandId)
    {
      case SpeckleV3Tool.COMMAND:
        return new NAV.Plugins.CommandState(true);
      case SpeckleV2Tool.COMMAND:
      {
        // Find the v2 plugin
        NAV.Plugins.PluginRecord? v2Plugin = PluginUtilities.FindV2Plugin();
        s_isV2PluginAvailable = v2Plugin != null;

        // Pass the plugin to the method for managing ribbon visibility
        HideV2RibbonTab();

        return new NAV.Plugins.CommandState((bool)s_isV2PluginAvailable);
      }
      default:
        return new NAV.Plugins.CommandState(false);
    }
  }

  private static void HideV2RibbonTab()
  {
    if (s_isV2RibbonHidden)
    {
      return; // Skip if already hidden.
    }

    var v2RibbonTab = Autodesk.Windows.ComponentManager.Ribbon.Tabs.FirstOrDefault(tab =>
      tab.Id == SpeckleV2Tool.RIBBON_TAB_ID + SpeckleV2Tool.PLUGIN_SUFFIX
    );

    if (v2RibbonTab == null)
    {
      return;
    }

    Autodesk.Windows.ComponentManager.Ribbon.Tabs.Remove(v2RibbonTab);
    s_isV2RibbonHidden = true; // Mark as hidden to avoid redundant calls.
  }

  /// <summary>
  /// Executes the specified command after validating the Navisworks version.
  /// </summary>
  /// <param name="commandId">The command to execute.</param>
  /// <param name="parameters">Additional command parameters.</param>
  /// <returns>0 if successful, non-zero otherwise.</returns>
  public override int ExecuteCommand(string commandId, params string[] parameters)
  {
    if (!IsValidVersion())
    {
      return 0;
    }

    switch (commandId)
    {
      case SpeckleV3Tool.COMMAND:
        HandleCommand(SpeckleV3Tool.PLUGIN, commandId);
        break;

      case SpeckleV2Tool.COMMAND:
        HandleCommand(SpeckleV2Tool.PLUGIN, $"{SpeckleV2Tool.PLUGIN}.{SpeckleV2Tool.DEVELOPER_ID}");
        break;
      default:
      {
        MessageBox.Show($"You have clicked on an unexpected command with ID = '{commandId}'");
        break;
      }
    }

    return 0;
  }

  private static void HandleCommand(string pluginId, string commandId)
  {
    if (PluginUtilities.ShouldSkipLoad(pluginId, commandId, true))
    {
      return;
    }

    var pluginRecord = NavisworksApp.Plugins.FindPlugin(pluginId + SpeckleV3Tool.PLUGIN_SUFFIX);
    if (pluginRecord == null)
    {
      return;
    }

    _ = pluginRecord.LoadedPlugin ?? pluginRecord.LoadPlugin();
    PluginUtilities.ActivatePluginPane(pluginRecord);
  }

  private static bool IsValidVersion()
  {
    if (NavisworksApp.Version.RuntimeProductName.Contains(SpeckleV3Tool.Version.ToString().Replace("v", "")))
    {
      return true;
    }

    MessageBox.Show(
      $"This Add-In was built for Navisworks {SpeckleV3Tool.Version}, "
        + $"please contact support@speckle.systems for assistance...",
      "Cannot Continue!",
      MessageBoxButtons.OK,
      MessageBoxIcon.Error
    );
    return false;
  }
}
