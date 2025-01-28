using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
#if DEBUG
using System.Text;
#endif

namespace Speckle.Connector.Navisworks.Plugin;

[
  NAV.Plugins.Plugin("SpeckleNavisworksNextGen", "Speckle", DisplayName = "Speckle (Beta)"),
  NAV.Plugins.Strings("NavisworksRibbon.name"),
  NAV.Plugins.RibbonLayout("NavisworksRibbon.xaml"),
  NAV.Plugins.RibbonTab("Speckle", DisplayName = "Speckle", LoadForCanExecute = true),
  NAV.Plugins.Command(
    LaunchSpeckleConnector.COMMAND,
    LoadForCanExecute = true,
    Icon = "Resources/s2logo16.png",
    LargeIcon = "Resources/s2logo32.png",
    ToolTip = "Next Gen Speckle Connector (Beta) for Navisworks",
    DisplayName = "Speckle (Beta)"
  ),
]
[SuppressMessage(
  "design",
  "CA1812:Avoid uninstantiated internal classes",
  Justification = "Instantiated by Navisworks"
)]
internal sealed class RibbonHandler : NAV.Plugins.CommandHandlerPlugin
{
  // ReSharper disable once CollectionNeverQueried.Local
  private static readonly Dictionary<NAV.Plugins.Plugin, bool> s_loadedPlugins = [];

  /// <summary>
  /// Determines the state of a command in Navisworks.
  /// </summary>
  /// <param name="commandId">The ID of the command to check.</param>
  /// <returns>The state of the command.</returns>
  public override NAV.Plugins.CommandState CanExecuteCommand(string commandId) =>
    commandId == LaunchSpeckleConnector.COMMAND
      ? new NAV.Plugins.CommandState(true)
      : new NAV.Plugins.CommandState(false);

  /// <summary>
  /// Loads a plugin in Navisworks.
  /// </summary>
  /// <param name="plugin">The name of the plugin to load.</param>
  /// <param name="notAutomatedCheck">Optional. Specifies whether to check if the application is automated. Default is true.</param>
  /// <param name="command">Optional. The command associated with the plugin. Default is an empty string.</param>
  private static void LoadPlugin(string plugin, bool notAutomatedCheck = true, string command = "")
  {
    if (ShouldSkipLoad(notAutomatedCheck))
    {
      return;
    }

    if (ShouldSkipPluginLoad(plugin, command))
    {
      return;
    }

    var pluginRecord = NavisworksApp.Plugins.FindPlugin(plugin + ".Speckle");
    if (pluginRecord is null)
    {
      return;
    }

    var loadedPlugin = pluginRecord.LoadedPlugin ?? pluginRecord.LoadPlugin();

    ActivatePluginPane(pluginRecord, loadedPlugin, command);
  }

  /// <summary>
  /// Checks whether the load should be skipped based on the notAutomatedCheck flag and application automation status.
  /// </summary>
  /// <param name="notAutomatedCheck">The flag indicating whether to check if the application is automated.</param>
  /// <returns>True if the load should be skipped, False otherwise.</returns>
  private static bool ShouldSkipLoad(bool notAutomatedCheck) => notAutomatedCheck && NavisworksApp.IsAutomated;

  /// <summary>
  /// Checks whether the plugin load should be skipped based on the plugin and command values.
  /// </summary>
  /// <param name="plugin">The name of the plugin.</param>
  /// <param name="command">The command associated with the plugin.</param>
  /// <returns>True if the plugin load should be skipped, False otherwise.</returns>
  private static bool ShouldSkipPluginLoad(string plugin, string command) =>
    string.IsNullOrEmpty(plugin) || string.IsNullOrEmpty(command);

  /// <summary>
  /// Activates the plugin's pane if it is of the right type.
  /// </summary>
  /// <param name="pluginRecord">The plugin record.</param>
  /// <param name="loadedPlugin">The loaded plugin instance.</param>
  /// <param name="command">The command associated with the plugin.</param>
  private static void ActivatePluginPane(NAV.Plugins.PluginRecord pluginRecord, object loadedPlugin, string command)
  {
    if (ShouldActivatePluginPane(pluginRecord))
    {
      var dockPanePlugin = (NAV.Plugins.DockPanePlugin)loadedPlugin;
      dockPanePlugin.ActivatePane();

      s_loadedPlugins[dockPanePlugin] = true;
    }
    else
    {
#if DEBUG
      ShowPluginInfoMessageBox();
      ShowPluginNotLoadedMessageBox(command);
#endif
    }
  }

  /// <summary>
  /// Checks whether the plugin's pane should be activated based on the plugin record.
  /// </summary>
  /// <param name="pluginRecord">The plugin record.</param>
  /// <returns>True if the plugin's pane should be activated, False otherwise.</returns>
  private static bool ShouldActivatePluginPane(NAV.Plugins.PluginRecord pluginRecord) =>
    pluginRecord.IsLoaded && pluginRecord is NAV.Plugins.DockPanePluginRecord && pluginRecord.IsEnabled;

  public override int ExecuteCommand(string commandId, params string[] parameters)
  {
    // ReSharper disable once RedundantAssignment
    var buildVersion = string.Empty;

#if NAVIS2020
    buildVersion = "2020";
#endif
#if NAVIS2021
    buildVersion = "2021";
#endif
#if NAVIS2022
    buildVersion = "2022";
#endif
#if NAVIS2023
    buildVersion = "2023";
#endif
#if NAVIS2024
    buildVersion = "2024";
#endif
#if NAVIS2025
    buildVersion = "2025";
#endif

    // Version
    if (!NavisworksApp.Version.RuntimeProductName.Contains(buildVersion))
    {
      MessageBox.Show(
        "This Add-In was built for Navisworks "
          + buildVersion
          + ", please contact jonathon@speckle.systems for assistance...",
        "Cannot Continue!",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error
      );
      return 0;
    }

    switch (commandId)
    {
      case LaunchSpeckleConnector.COMMAND:
      {
        LoadPlugin(LaunchSpeckleConnector.PLUGIN, command: commandId);
        break;
      }

      default:
      {
        MessageBox.Show("You have clicked on an unexpected command with ID = '" + commandId + "'");
        break;
      }
    }

    return 0;
  }

#if DEBUG
  /// <summary>
  /// Shows a message box displaying plugin information.
  /// </summary>
  private static void ShowPluginInfoMessageBox()
  {
    var sb = new StringBuilder();
    foreach (var pr in NavisworksApp.Plugins.PluginRecords)
    {
      sb.AppendLine(pr.Name + ": " + pr.DisplayName + ", " + pr.Id);
    }

    MessageBox.Show(sb.ToString());
  }

  /// <summary>
  /// Shows a message box indicating that the plugin was not loaded.
  /// </summary>
  /// <param name="command">The command associated with the plugin.</param>
  private static void ShowPluginNotLoadedMessageBox(string command) => MessageBox.Show(command + " Plugin not loaded.");
#endif
}
