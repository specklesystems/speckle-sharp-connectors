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

  // NOTE: Somehow tekla triggers this class twice at the beginning and on first dialog our webview appears
  // with small size of render in Host even if we set it as Dock.Fill. But on second trigger dialog initializes as expected.
  // So, we do not init our plugin at first attempt, we just close it at first.
  // On second, we init plugin and mark plugin as 'Initialized' to handle later init attempts nicely.
  // We make 'IsInitialized' as 'false' only whenever our main dialog is closed explicitly by user.
  private static bool IsFirst { get; set; } = true;
  public static bool IsInitialized { get; private set; }

  //window owner call
  [DllImport("user32.dll", SetLastError = true)]
  [SuppressMessage("Security", "CA5392:Use DefaultDllImportSearchPaths attribute for P/Invokes")]
  private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);

  private const int GWL_HWNDPARENT = -8;

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

#else
    throw new NotImplementedException();
#endif
  }
}
