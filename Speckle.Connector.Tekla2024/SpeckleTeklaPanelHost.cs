using System.Windows.Forms.Integration;
using Speckle.Connector.Tekla2024.Plugin;
using Speckle.Connectors.DUI.WebView;
using Tekla.Structures.Dialog;
using Tekla.Structures.Model;

namespace Speckle.Connector.Tekla2024;

public class SpeckleTeklaPanelHost : PluginFormBase
{
  private ElementHost Host { get; }
  public Model Model { get; private set; }

  public SpeckleTeklaPanelHost()
  {
    Model = new Model(); // don't know what is this..
    var webview = TeklaPlugin.Container.Resolve<DUI3ControlWebView>();
    Host = new() { Child = webview };
    Controls.Add(Host);
  }
}
