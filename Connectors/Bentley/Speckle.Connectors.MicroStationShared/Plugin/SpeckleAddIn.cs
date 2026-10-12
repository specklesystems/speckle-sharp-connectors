using System.Windows;
using Bentley.MstnPlatformNET;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.MicroStation.DependencyInjection;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converters.MicroStation.DependencyInjection;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// In-process MicroStation-platform add-in entry point. Shared by every Bentley product
/// (MicroStation, OpenRoads Designer, OpenBridge Designer, …) — the per-product DLL supplies
/// its own <see cref="SpeckleAddInIdentity"/> partial that fills in MdlTaskId, slug, host app, and version.
/// MicroStation loads this DLL via MS_DGNAPPS and instantiates this class using the
/// private <c>(IntPtr mdlDesc)</c> constructor required by the <see cref="AddIn"/> base class.
/// Keyin handlers are declared in the embedded <c>CommandTable.xml</c> resource.
/// Open the panel via keyin: <c>Speckle show</c>
/// </summary>
[AddIn(MdlTaskID = SpeckleAddInIdentity.MDL_TASK_ID)]
public class SpeckleAddIn : AddIn
{
  // Static constructor runs before any instance is created — registers the assembly resolver
  // so Speckle DLLs in the deploy directory are found during DLL load, before Run() is called.
  static SpeckleAddIn()
  {
    AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<SpeckleAddIn>;
  }

  // Required constructor signature — MicroStation passes the MDL descriptor handle.
  private SpeckleAddIn(IntPtr mdlDesc)
    : base(mdlDesc) { }

  internal static ServiceProvider? Container { get; private set; }

  protected override int Run(string[] commandLine)
  {
    return 0; // Container is initialized lazily on first ShowPanel call
  }

  /// <summary>
  /// Keyin handler (`Speckle probe`) — writes the offline-verification report to
  /// %TEMP%\speckle-msprobe.log. See <see cref="ProbeCommand"/>.
  /// </summary>
  public static void Probe(string unparsed)
  {
    try
    {
      ProbeCommand.Run(unparsed);
    }
    catch (Exception ex)
    {
      MessageBox.Show($"Speckle probe failed:\n\n{ex.Message}", "Speckle", MessageBoxButton.OK, MessageBoxImage.Error);
    }
  }

  /// <summary>
  /// Keyin handler — referenced by Function attribute in CommandTable.xml.
  /// Opens the Speckle panel, or brings it to front if already open.
  /// </summary>
  public static void ShowPanel(string _)
  {
    if (SpeckleWindow.IsOpen)
    {
      SpeckleWindow.BringToFront();
      return;
    }

    try
    {
      if (Container == null)
      {
        var services = new ServiceCollection();
        services.Initialize(SpeckleAddInIdentity.HostApp, SpeckleAddInIdentity.VERSION);
        services.AddMicroStation();
        services.AddMicroStationConverters();
        Container = services.BuildServiceProvider();
        Container.UseDUI();
        Container.GetRequiredService<MicroStationDocumentEvents>();
      }

      SpeckleWindow.Show(Container);
    }
    catch (Exception ex)
    {
      MessageBox.Show(
        $"Speckle failed to open:\n\n{ex.Message}",
        "Speckle Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error
      );
    }
  }
}
