using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
using Speckle.Connector.Navisworks.Plugin.Tools;

namespace Speckle.Connector.Navisworks.Plugin;

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
[SuppressMessage(
  "design",
  "CA1812:Avoid uninstantiated internal classes",
  Justification = "Instantiated by Navisworks"
)]
internal sealed class RibbonHandler : NAV.Plugins.CommandHandlerPlugin
{
  private static bool? s_isV2PluginAvailable; // Nullable to indicate uncached state.

  static RibbonHandler()
  {
    // Subscribe to the static PluginRecordsChanged event
    NAV.ApplicationParts.ApplicationPlugins.PluginRecordsChanged += OnPluginRecordsChanged;
  }

  private static void OnPluginRecordsChanged(object sender, EventArgs e) => s_isV2PluginAvailable = null;

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

  private static void HideV2RibbonTab() =>
    Autodesk.Windows.ComponentManager.Ribbon.Tabs.Remove(
      Autodesk.Windows.ComponentManager.Ribbon.Tabs.FirstOrDefault(tab =>
        tab.Id == SpeckleV2Tool.RIBBON_TAB_ID + SpeckleV2Tool.PLUGIN_SUFFIX
      )
    );

  public override int ExecuteCommand(string commandId, params string[] parameters)
  {
    if (!IsValidVersion())
    {
      return 0;
    }

    switch (commandId)
    {
      case SpeckleV3Tool.COMMAND:
      {
        if (!PluginUtilities.ShouldSkipLoad(SpeckleV3Tool.PLUGIN, commandId, true))
        {
          var pluginRecord = NavisworksApp.Plugins.FindPlugin(SpeckleV3Tool.PLUGIN + SpeckleV3Tool.PLUGIN_SUFFIX);
          if (pluginRecord != null)
          {
            _ = pluginRecord.LoadedPlugin ?? pluginRecord.LoadPlugin();
            PluginUtilities.ActivatePluginPane(pluginRecord, commandId);
          }
        }
        break;
      }

      case SpeckleV2Tool.COMMAND:
      {
        if (!PluginUtilities.ShouldSkipLoad(SpeckleV2Tool.PLUGIN, commandId, true))
        {
          var pluginRecord = NavisworksApp.Plugins.FindPlugin(SpeckleV2Tool.PLUGIN + SpeckleV2Tool.PLUGIN_SUFFIX);
          if (pluginRecord != null)
          {
            _ = pluginRecord.LoadedPlugin ?? pluginRecord.LoadPlugin();
            PluginUtilities.ActivatePluginPane(pluginRecord, $"{SpeckleV2Tool.PLUGIN}.{SpeckleV2Tool.DEVELOPER_ID}");
          }
          else
          {
            MessageBox.Show("Unable to find plugin for Speckle v2.");
          }
        }
        break;
      }
      default:
      {
        MessageBox.Show($"You have clicked on an unexpected command with ID = '{commandId}'");
        break;
      }
    }

    return 0;
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
