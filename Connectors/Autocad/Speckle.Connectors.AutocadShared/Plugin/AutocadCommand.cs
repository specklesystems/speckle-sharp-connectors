using System.Drawing;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.WebView;
#if AUTOCAD
using Speckle.Connectors.Autocad.DependencyInjection;
using Speckle.Converters.Autocad;
#elif CIVIL3D
using Speckle.Converters.Civil3dShared;
using Speckle.Connectors.Civil3dShared.DependencyInjection;
#endif
namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadCommand
{
  private static PaletteSet? PaletteSet { get; set; }
  private static readonly Guid s_id = new("7C27DD2B-86E8-4D31-B3DE-B34B267B1DC8");
  public ServiceProvider? Container { get; private set; }
  private IDisposable? _disposableLogger;
  public const string COMMAND_STRING = "Speckle";

  [CommandMethod(COMMAND_STRING)]
  public void Command()
  {
    if (PaletteSet != null)
    {
      FocusPalette();
      return;
    }

    PaletteSet = new PaletteSet($"Speckle", s_id)
    {
      Size = new Size(400, 500),
      DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right),
    };

    // init DI
    var services = new ServiceCollection();
    _disposableLogger = services.Initialize(AppUtils.App, AppUtils.Version);
#if AUTOCAD
    services.AddAutocad();
    services.AddAutocadConverters();
#elif CIVIL3D
    services.AddCivil3d();
    services.AddCivil3dConverters();
#endif
    Container = services.BuildServiceProvider();
    Container.UseDUI();

    var panelWebView = Container.GetRequiredService<DUI3ControlWebView>();

    PaletteSet.AddVisual("Speckle", panelWebView);

    FocusPalette();
  }

  private void FocusPalette()
  {
    if (PaletteSet != null)
    {
      PaletteSet.KeepFocus = true;
      PaletteSet.Visible = true;
    }
  }
}
