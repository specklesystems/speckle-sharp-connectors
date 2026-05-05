using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.MicroStation.DependencyInjection;
using Speckle.Connectors.MicroStation.HostApp;
using Speckle.Converter.MicroStation.DependencyInjection;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// WPF application entry point for the Speckle MicroStation 2026 connector.
/// Run this exe alongside a running MicroStation 2026 session.
/// It connects to MicroStation via COM and opens the Speckle DUI3 panel.
/// </summary>
public sealed class SpeckleApp : System.Windows.Application
{
  [STAThread]
  public static void Main()
  {
    try
    {
      // Register assembly resolver before ANY Bentley types are used so the CLR
      // can find Bentley.DgnPlatformNET and any other DLLs not in our output folder.
      AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
      var app = new SpeckleApp();
      app.Run();
    }
    catch (Exception ex)
    {
      MessageBox.Show(
        $"Speckle failed to start:\n\n{ex}",
        "Speckle Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error
      );
    }
  }

  private static System.Reflection.Assembly? ResolveAssembly(object? sender, ResolveEventArgs args)
  {
    // Probe MicroStation's install directories for any unresolved Bentley DLLs.
    var msDir = @"C:\Program Files\Bentley\MicroStation 2026\MicroStation\";
    var name = new System.Reflection.AssemblyName(args.Name).Name + ".dll";
    foreach (var probe in new[] { msDir, System.IO.Path.Combine(msDir, "Assemblies\\") })
    {
      var path = System.IO.Path.Combine(probe, name);
      if (System.IO.File.Exists(path))
      {
        return System.Reflection.Assembly.LoadFrom(path);
      }
    }
    return AssemblyResolver.OnAssemblyResolve<SpeckleApp>(sender, args);
  }

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    // AssemblyResolve already registered in Main() — no need to re-register here.

    var msApp = MsApp.TryGetInstance();
    if (msApp == null)
    {
      MessageBox.Show(
        "MicroStation 2026 is not running.\n\nPlease start MicroStation first, then launch the Speckle connector.",
        "Speckle — MicroStation not found",
        MessageBoxButton.OK,
        MessageBoxImage.Warning
      );
      Shutdown();
      return;
    }

    try
    {
      var services = new ServiceCollection();
      services.Initialize(HostApplications.MicroStation, SpeckleToolConstants.Version);
      services.AddMicroStation();
      services.AddMicroStationConverters();

      var container = services.BuildServiceProvider();
      container.UseDUI();
      container.GetRequiredService<MicroStationDocumentEvents>();

      var window = SpeckleWindow.CreateAndShow(container);
      window.Closed += (_, _) => Shutdown();
    }
    catch (Exception ex)
    {
      MessageBox.Show(
        $"Speckle failed to start:\n\n{ex.Message}",
        "Speckle Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error
      );
      Shutdown();
    }
  }
}
