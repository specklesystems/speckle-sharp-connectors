using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.DependencyInjection;
using Speckle.Connector.Navisworks.HostApp;
using Speckle.Connector.Navisworks.Plugin.Tools;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converter.Navisworks.DependencyInjection;

namespace Speckle.Connector.Navisworks.Plugin;

[
  NAV.Plugins.DockPanePlugin(450, 750, FixedSize = false, AutoScroll = true, MinimumHeight = 410, MinimumWidth = 250),
  NAV.Plugins.Plugin(
    SpeckleV3Tool.PLUGIN,
    SpeckleV3Tool.DEVELOPER_ID,
    DisplayName = SpeckleV3Tool.DISPLAY_NAME,
    Options = NAV.Plugins.PluginOptions.None,
    ToolTip = "Speckle Connector for Navisworks",
    ExtendedToolTip = "Next Gen Speckle Connector for Navisworks"
  )
]
[SuppressMessage(
  "design",
  "CA1812:Avoid uninstantiated internal classes",
  Justification = "Instantiated by Navisworks"
)]
internal sealed class Connector : NAV.Plugins.DockPanePlugin
{
  private ServiceProvider? Container { get; set; }

  public override Control CreateControlPane()
  {
    AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<Connector>;

    var services = new ServiceCollection();

    services.Initialize(HostApplications.Navisworks, SpeckleV3Tool.Version);

    services.AddNavisworks();
    services.AddNavisworksConverter();

    Container = services.BuildServiceProvider();
    Container.UseDUI();
    Container.GetRequiredService<NavisworksDocumentEvents>();
    Container.GetRequiredService<ISerializationOptions>().SkipCacheRead = true;
    Container.GetRequiredService<ISerializationOptions>().SkipCacheWrite = true;

    var u = Container.GetRequiredService<DUI3ControlWebView>();

    var speckleHost = new ElementHost { AutoSize = true, Child = u };

    speckleHost.CreateControl();

    return speckleHost;
  }

  public override void DestroyControlPane(Control pane)
  {
    if (pane is UserControl control)
    {
      control.Dispose();
    }
  }
}
