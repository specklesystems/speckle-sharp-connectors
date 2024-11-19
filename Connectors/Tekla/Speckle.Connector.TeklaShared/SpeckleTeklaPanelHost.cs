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
  private ElementHost Host { get; }
  public Model Model { get; private set; }
  public static new ServiceProvider? Container { get; private set; }
  private static readonly List<SpeckleTeklaPanelHost> s_instances = new();

  public SpeckleTeklaPanelHost()
  {
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

    // adds instances to tracking list
    s_instances.Add(this);

    if (s_instances.Count > 1)
    {
      var firstInstance = s_instances[0];
      s_instances.RemoveAt(0);
      // hides the first instance if there is more than one
      firstInstance.Hide();
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
#elif TEKLA2023
    return HostAppVersion.v2023;
#else
    throw new NotImplementedException();
#endif
  }
}
