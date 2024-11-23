using System.Windows.Forms.Integration;
using CSiAPIv1;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI.WebView;
using Speckle.Sdk.Host;

namespace Speckle.Connector.ETABS22;

public class Form1 : Form
{
  private ElementHost Host { get; set; }
  public static new ServiceProvider? Container { get; set; }
  private cSapModel _sapModel;
  private cPluginCallback _pluginCallback;

  public Form1()
  {
    var services = new ServiceCollection();
    services.Initialize(HostApplications.ETABS, GetVersion());
    services.AddETABS();

    Container = services.BuildServiceProvider();

    var webview = Container.GetRequiredService<DUI3ControlWebView>();
    Host = new() { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(Host);
    FormClosing += Form1Closing;
  }

  public void SetSapModel(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
  {
    _sapModel = sapModel;
    _pluginCallback = pluginCallback;
  }

  public void Form1Closing(object? sender, FormClosingEventArgs e)
  {
    Host.Dispose();
    _pluginCallback.Finish(0);
  }

  private static HostAppVersion GetVersion()
  {
    return HostAppVersion.v2022;
  }
}
