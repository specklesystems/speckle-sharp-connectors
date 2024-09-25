using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connector.Tekla2024.Plugin;
using Speckle.Connectors.DUI.WebView;
using Tekla.Structures.Dialog;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;

namespace Speckle.Connector.Tekla2024;

public class SpeckleTeklaPanelHost : PluginFormBase
{
  private ElementHost Host { get; }
  public Model Model { get; private set; }

  public SpeckleTeklaPanelHost()
  {
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
    var webview = TeklaPlugin.Container.GetRequiredService<DUI3ControlWebView>();
    Host = new() { Child = webview };
    Controls.Add(Host);
    Operation.DisplayPrompt("Speckle connector initialized.");

    Show();
    Activate();
    Focus();
  }
}
