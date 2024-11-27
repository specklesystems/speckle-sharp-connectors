using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.CSiShared;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI.WebView;
using Speckle.Sdk.Host;

// NOTE: Plugin entry point must match the assembly name, otherwise hits you with a "Not found" error when loading plugin
// TODO: Move ETABS implementation to csproj as part of CNX-835 and/or CNX-828
namespace Speckle.Connectors.ETABS22;

public class Form1 : Form
{
  private ElementHost Host { get; set; }
  public static new ServiceProvider? Container { get; set; }
  private cSapModel _sapModel;
  private cPluginCallback _pluginCallback;

  public Form1()
  {
    this.Text = "Speckle (Beta)";

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

    // NOTE: Update the form to initialize the CSiSharedApplicationService when we receive "sapModel"
    // Ensures service ready to use by other components
    var csiService = Container!.GetRequiredService<ICSiApplicationService>();
    csiService.Initialize(sapModel, pluginCallback);
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
