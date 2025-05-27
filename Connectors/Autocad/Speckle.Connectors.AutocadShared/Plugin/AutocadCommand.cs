using System.Drawing;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Microsoft.Extensions.DependencyInjection;
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
  private static readonly Guid s_id = new("3223E594-1B09-4E54-B3DD-8EA0BECE7BA5");
  public ServiceProvider? Container { get; private set; }
  public const string COMMAND_STRING = "SpeckleBeta";

  [CommandMethod(COMMAND_STRING)]
  public void Command()
  {
    if (PaletteSet != null)
    {
      FocusPalette();
      return;
    }

    PaletteSet = new PaletteSet($"Speckle (Beta)", s_id)
    {
      Size = new Size(400, 500),
      DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right)
    };

    // init DI
    var services = new ServiceCollection();
#if AUTOCAD
    services.AddAutocad(AppUtils.Version);
    services.AddAutocadConverters();
#elif CIVIL3D
    services.AddCivil3d(AppUtils.Version);
    services.AddCivil3dConverters();
#endif
    Container = services.BuildServiceProvider();
    Container.UseDUI();

    var panelWebView = Container.GetRequiredService<DUI3ControlWebView>();

    PaletteSet.AddVisual("Speckle (Beta)", panelWebView);

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
