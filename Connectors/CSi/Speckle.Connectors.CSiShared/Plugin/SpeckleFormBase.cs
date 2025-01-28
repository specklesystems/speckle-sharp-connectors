using System.ComponentModel;
using System.Reflection;
using System.Timers;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.CSiShared;
using Speckle.Sdk.Host;
using Timer = System.Timers.Timer;

namespace Speckle.Connectors.CSiShared;

[DesignerCategory("")]
public abstract class SpeckleFormBase : Form, ICsiApplicationService
{
  private ElementHost Host { get; set; }
  private cPluginCallback _pluginCallback;
  private readonly Timer _modelCheckTimer;
  private string _lastModelFilename = string.Empty;
#pragma warning disable CA2213
  private ServiceProvider _container;
#pragma warning restore CA2213
  private bool _disposed;

  protected SpeckleFormBase()
  {
    Text = "Speckle (Beta)";
    Size = new Size(400, 600);

    // pole every second to check if user switched to a different model (no other way)
    // we need to monitor performance affects. polling for model changes and polling for changes in selection (CsiSharedSelectionBinding)
    _modelCheckTimer = new Timer(1000);
    _modelCheckTimer.Elapsed += CheckModelChanges;
    _modelCheckTimer.Start();
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
    // store app-specific model and callback references (callback if at all possible?)
    SapModel = sapModel;
    _pluginCallback = pluginCallback;

    string assemblyName =
      Assembly.GetExecutingAssembly().GetName().Name
      ?? throw new InvalidOperationException("Could not determine executing assembly name");
    string resourcePath = $"{assemblyName}.Resources.et_element_Speckle.bmp";

    // load and set the speckle icon from embedded resources
    using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath))
    {
      if (stream == null)
      {
        throw new InvalidOperationException($"Could not find resource: {resourcePath}");
      }

      using var bmp = new Bitmap(stream);
      Icon = Icon.FromHandle(bmp.GetHicon());
    }

    // configure dependency injection services
    var services = new ServiceCollection();
    services.AddSingleton<ICsiApplicationService>(this);
    ConfigureServices(services);

    // build service container and initialize ui framework
    _container = services.BuildServiceProvider();
    _container.UseDUI();

    // setup webview control and form properties
    var webview = _container.GetRequiredService<DUI3ControlWebView>();
    Host = new() { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(Host);
    FormBorderStyle = FormBorderStyle.Sizable;
    FormClosing += Form1Closing;
  }

  private void Form1Closing(object? sender, FormClosingEventArgs e) => _pluginCallback.Finish(0);

  // checks if user has switched to a different model
  private void CheckModelChanges(object? sender, ElapsedEventArgs e)
  {
    string currentModelFilename = SapModel.GetModelFilename();

    if (string.IsNullOrEmpty(currentModelFilename))
    {
      return; // new model might be loading - this transition state will be caught here
    }

    if (currentModelFilename != _lastModelFilename && !string.IsNullOrEmpty(_lastModelFilename))
    {
      BeginInvoke(Close);
    }
    else if (string.IsNullOrEmpty(_lastModelFilename))
    {
      _lastModelFilename = currentModelFilename; // first time initialization
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (!_disposed)
    {
      if (disposing)
      {
        _modelCheckTimer.Dispose();
        Host.Dispose();
        _container.Dispose();
      }
      _disposed = true;
    }
    base.Dispose(disposing);
  }
}
