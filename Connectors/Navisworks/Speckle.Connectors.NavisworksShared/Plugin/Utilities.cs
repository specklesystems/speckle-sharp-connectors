using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using Speckle.Connector.Navisworks.Plugin.Tools;

namespace Speckle.Connector.Navisworks.Plugin;

internal static class PluginUtilities
{
  public static bool ShouldSkipLoad(string plugin, string command, bool notAutomatedCheck) =>
    notAutomatedCheck && NavisworksApp.IsAutomated || string.IsNullOrEmpty(plugin) || string.IsNullOrEmpty(command);

  internal static PluginRecord? FindV2Plugin()
  {
    var pluginRecords = NavisworksApp.Plugins.PluginRecords;
    var v2Plugin = pluginRecords.FirstOrDefault(p => p.Id == SpeckleV2Tool.RIBBON_TAB_ID);

    return v2Plugin ?? null;
  }

  public static void ActivatePluginPane(PluginRecord? pluginRecord, string command)
  {
    if (pluginRecord is null || !pluginRecord.IsEnabled || !pluginRecord.IsLoaded)
    {
#if DEBUG
      MessageBox.Show($"Command '{command}' cannot activate plugin pane. Plugin state invalid.");
#endif
      return;
    }

    if (pluginRecord.LoadedPlugin is DockPanePlugin dockPanePlugin)
    {
      dockPanePlugin.ActivatePane();
    }
  }
}
