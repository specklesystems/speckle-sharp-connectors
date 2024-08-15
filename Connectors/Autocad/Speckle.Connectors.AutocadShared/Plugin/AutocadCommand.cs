using System.Drawing;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Utils;
using Speckle.Sdk.Common;
using Speckle.Sdk.Host;

namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadCommand
{
  private static PaletteSet? PaletteSet { get; set; }
  private static readonly Guid s_id = new("3223E594-1B09-4E54-B3DD-8EA0BECE7BA5");

  public SpeckleContainer? Container { get; private set; }
  private IDisposable? _disposableLogger;

  [CommandMethod("SpeckleNewUI")]
  public void Command()
  {
    if (PaletteSet != null)
    {
      FocusPalette();
      return;
    }

    PaletteSet = new PaletteSet("Speckle DUI3", s_id)
    {
      Size = new Size(400, 500),
      DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right)
    };

    var builder = SpeckleContainerBuilder.CreateInstance();
    // init DI
    _disposableLogger = Connector.Initialize(GetApp(), GetVersion());
    Container = builder
      .LoadAutofacModules(
        Assembly.GetExecutingAssembly(),
        [Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).NotNull()]
      )
      .Build();

    var panelWebView = Container.Resolve<DUI3ControlWebView>();

    PaletteSet.AddVisual("Speckle DUI3 WebView", panelWebView);

    FocusPalette();
  }

  private HostApplication GetApp()
  {
#if CIVIL3D
    return HostApplications.Civil3D;
#elif AUTOCAD
    return HostApplications.AutoCAD;
#else
    throw new NotImplementedException();
#endif
  }

  private HostAppVersion GetVersion()
  {
#if CIVIL3D2024
    return HostAppVersion.v2024;
#elif AUTOCAD2025
    return HostAppVersion.v2025;
#elif AUTOCAD2024
    return HostAppVersion.v2024;
#elif AUTOCAD2023
    return HostAppVersion.v2023;
#elif AUTOCAD2022
    return HostAppVersion.v2022;
#else
    throw new NotImplementedException();
#endif
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
