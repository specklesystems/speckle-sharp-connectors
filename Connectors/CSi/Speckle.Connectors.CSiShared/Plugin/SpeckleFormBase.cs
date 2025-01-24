using System.ComponentModel;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Host;

namespace Speckle.Connectors.CSiShared;

[DesignerCategory("")]
public abstract class SpeckleFormBase : Form, ICsiApplicationService
{
  private ElementHost Host { get; set; }
  private cPluginCallback _pluginCallback;
#pragma warning disable CA2213
  private ServiceProvider _container;
#pragma warning restore CA2213

  protected SpeckleFormBase()
  {
    Text = "Speckle (Beta)";
  }

  public cSapModel SapModel { get; private set; }

  protected virtual void ConfigureServices(IServiceCollection services)
  {
    services.Initialize(GetHostApplication(), GetVersion());
    services.AddCsi();
    services.AddCsiConverters();
  }

  protected abstract HostApplication GetHostApplication();

  protected abstract HostAppVersion GetVersion();

  public void Initialize(ref cSapModel sapModel, ref cPluginCallback pluginCallback)
  {
    SapModel = sapModel;
    _pluginCallback = pluginCallback;

    var services = new ServiceCollection();
    services.AddSingleton<ICsiApplicationService>(this);
    ConfigureServices(services);

    _container = services.BuildServiceProvider();
    _container.UseDUI();

    var webview = _container.GetRequiredService<DUI3ControlWebView>();
    Host = new() { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(Host);
    FormBorderStyle = FormBorderStyle.Sizable;
    FormClosing += Form1Closing;
  }

  private void Form1Closing(object? sender, FormClosingEventArgs e)
  {
    Host.Dispose();
    _pluginCallback.Finish(0);
    _container.Dispose();
  }
}
