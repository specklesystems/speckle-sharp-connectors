using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converter.Tekla2024;
using Speckle.Sdk.Host;
using Tekla.Structures.Dialog;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;

namespace Speckle.Connector.Tekla2024;

public class SpeckleTeklaPanelHost : PluginFormBase
{
  private ElementHost Host { get; }
  public Model Model { get; private set; }

  public static new ServiceProvider? Container { get; private set; }

  // TODO: private IDisposable? _disposableLogger;

  public SpeckleTeklaPanelHost()
  {
    var services = new ServiceCollection();
    services.Initialize(HostApplications.TeklaStructures, GetVersion());
    services.AddTekla();
    services.AddTeklaConverters();

    // TODO: Add Tekla converters

    Container = services.BuildServiceProvider();

    Model = new Model(); // don't know what is this..
    if (!Model.GetConnectionStatus())
    {
      MessageBox.Show(
        "Speckle connector connection failed. Please try again.",
        "Error",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error
      );
    }
    var webview = Container.GetRequiredService<DUI3ControlWebView>();
    Host = new() { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(Host);
    Operation.DisplayPrompt("Speckle connector initialized.");

    Show();
    Activate();
    Focus();
  }

  private HostAppVersion GetVersion()
  {
#if TEKLA2024
    return HostAppVersion.v2024;
#else
    throw new NotImplementedException();
#endif
  }
}
