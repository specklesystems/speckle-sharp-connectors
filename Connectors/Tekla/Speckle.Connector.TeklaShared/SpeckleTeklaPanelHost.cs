using System.Drawing;
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
  private static SpeckleTeklaPanelHost? s_instance;
  private ElementHost Host { get; set; }
  public Model Model { get; private set; }
  public static new ServiceProvider? Container { get; private set; }
  public static bool IsFirst { get; private set; } = true;
  public static bool IsInitialized { get; private set; }

  public SpeckleTeklaPanelHost()
  {
    if (IsFirst)
    {
      IsFirst = false;
      Close();
    }
    else
    {
      if (IsInitialized)
      {
        s_instance?.BringToFront();
        Close();
        return;
      }
      IsInitialized = true;
      InitializeInstance();
      s_instance?.BringToFront();
    }
  }

  protected override void OnClosed(EventArgs e)
  {
    s_instance?.Dispose();
    IsInitialized = false;
  }

  private void InitializeInstance()
  {
    s_instance = this; // Assign the current instance to the static field

    this.Text = "Speckle (Beta)";
    this.Name = "Speckle (Beta)";

    string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
    string resourcePath = $"{assemblyName}.Resources.et_element_Speckle.bmp";
    using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath))
    {
      if (stream == null)
      {
        throw new InvalidOperationException($"Could not find resource: {resourcePath}");
      }

      using var bmp = new Bitmap(stream);
      this.Icon = Icon.FromHandle(bmp.GetHicon());
    }

    var services = new ServiceCollection();
    services.Initialize(HostApplications.TeklaStructures, GetVersion());
    services.AddTekla();
    services.AddTeklaConverters();

    Container = services.BuildServiceProvider();

    Model = new Model();
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
    webview.RenderSize = new System.Windows.Size(800, 600);
    Host = new() { Child = webview, Dock = DockStyle.Fill };
    Controls.Add(Host);
    Operation.DisplayPrompt("Speckle connector initialized.");

    this.TopLevel = true;
    Show();
    Activate();
    Focus();
  }

  private HostAppVersion GetVersion()
  {
#if TEKLA2024
    return HostAppVersion.v2024;
#elif TEKLA2023
    return HostAppVersion.v2023;
#else
    throw new NotImplementedException();
#endif
  }
}
