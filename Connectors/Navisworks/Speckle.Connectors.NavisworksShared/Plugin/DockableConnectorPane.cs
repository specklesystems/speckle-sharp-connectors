using System.Diagnostics.CodeAnalysis;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Navisworks.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Sdk.Host;

namespace Speckle.Connector.Navisworks.NavisPlugin;

[
  DockPanePlugin(450, 750, FixedSize = false, AutoScroll = true, MinimumHeight = 410, MinimumWidth = 250),
  Plugin(
    LaunchSpeckleConnector.PLUGIN,
    "Speckle",
    DisplayName = "Speckle",
    Options = PluginOptions.None,
    ToolTip = "Speckle Connector for Navisworks",
    ExtendedToolTip = "Speckle Connector for Navisworks"
  )
]
[SuppressMessage(
  "design",
  "CA1812:Avoid uninstantiated internal classes",
  Justification = "Instantiated by Navisworks"
)]
internal sealed class Connector : DockPanePlugin
{
  public ServiceProvider? Container { get; private set; }

  public override Control CreateControlPane()
  {
    var services = new ServiceCollection();

    services.Initialize(HostApplications.Navisworks, HostAppVersion.v2024);

    services.AddNavisworks();

    Container = services.BuildServiceProvider();

    Container.UseDUI();

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
