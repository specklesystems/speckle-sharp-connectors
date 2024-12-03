using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Host;

namespace Speckle.Connectors.CSiShared;

public abstract class SpeckleFormBase : Form
{
  protected ElementHost Host { get; set; }
  public static new ServiceProvider? Container { get; set; }
  private cSapModel _sapModel;
  private cPluginCallback _pluginCallback;

  protected SpeckleFormBase()
  {
    Text = "Speckle (Beta)";

    var services = new ServiceCollection();
    services.Initialize(HostApplications.ETABS, GetVersion());
    services.AddCSi();
    services.AddCSiConverters();

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

    var csiService = Container.GetRequiredService<ICSiApplicationService>();
    csiService.Initialize(sapModel, pluginCallback);
  }

  protected void Form1Closing(object? sender, FormClosingEventArgs e)
  {
    Host.Dispose();
    _pluginCallback.Finish(0);
  }

  protected abstract HostAppVersion GetVersion();

  public new void ShowDialog()
  {
    base.ShowDialog();
  }
}
