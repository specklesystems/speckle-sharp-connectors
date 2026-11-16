using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Converters.TeklaShared;
using Tekla.Structures.Dialog;
using Tekla.Structures.Model;
using Tekla.Structures.Model.Operations;

namespace Speckle.Connectors.TeklaShared;

public class SpeckleTeklaPanelHost : PluginFormBase
{
  private static SpeckleTeklaPanelHost? s_instance;
  private ElementHost Host { get; set; }
  public Model Model { get; private set; }
  public static new ServiceProvider? Container { get; private set; }

  // Tekla constructs this form on every click (twice on the first) and shows it itself as a dialog.
  // Showing or closing that instance ourselves throws in Tekla, so it only stands in:
  // it opens our own panel, stays invisible and closes once Tekla has shown it.
  public static bool IsInitialized { get; private set; }

  //window owner call
  [DllImport("user32.dll", SetLastError = true)]
  [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes")]
  private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

  private const int GWL_HWNDPARENT = -8;

  public SpeckleTeklaPanelHost()
  {
    if (!IsInitialized)
    {
      IsInitialized = true;
      _ = new SpeckleTeklaPanelHost(true);
    }
    s_instance?.BringToFront();

    Opacity = 0;
    ShowInTaskbar = false;
    Shown += (_, _) => BeginInvoke(new Action(Close));
  }

  private SpeckleTeklaPanelHost(bool _) => InitializeInstance();

  protected override void OnClosed(EventArgs e)
  {
    if (this != s_instance)
    {
      return;
    }
    s_instance.Dispose();
    IsInitialized = false;
  }

  private void InitializeInstance()
  {
    s_instance = this; // Assign the current instance to the static field

    Text = "Speckle";
    Name = "Speckle";

    string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;
    string resourcePath = $"{assemblyName}.Resources.et_element_Speckle.bmp";
    using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath))
    {
      if (stream == null)
      {
        throw new InvalidOperationException($"Could not find resource: {resourcePath}");
      }

      using var bmp = new Bitmap(stream);
      Icon = Icon.FromHandle(bmp.GetHicon());
    }

    var services = new ServiceCollection();
    services.Initialize(HostApplications.TeklaStructures, GetVersion());
    services.AddTekla();
    services.AddTeklaConverters();

    Container = services.BuildServiceProvider();
    Container.UseDUI();

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

    TopLevel = true;
    SetWindowLongPtr(Handle, GWL_HWNDPARENT, MainWindow.Frame.Handle);
    Show();
    Activate();
    Focus();
  }

  private static HostAppVersion GetVersion()
  {
#if TEKLA2024
    return HostAppVersion.v2024;
#elif TEKLA2023
    return HostAppVersion.v2023;
#elif TEKLA2025
    return HostAppVersion.v2025;
#elif TEKLA2026
    return HostAppVersion.v2026;
#else
    throw new NotImplementedException();
#endif
  }
}
