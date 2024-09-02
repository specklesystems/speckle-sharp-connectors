using System.Drawing;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Utils;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Autocad.Plugin;

public class AutocadCommand
{
  private static PaletteSet? PaletteSet { get; set; }
  private static readonly Guid s_id = new("3223E594-1B09-4E54-B3DD-8EA0BECE7BA5");
  public SpeckleContainer? Container { get; private set; }
  private IDisposable? _disposableLogger;
  public const string COMMAND_STRING = "SpeckleBeta";

  [CommandMethod(COMMAND_STRING)]
  public void Command()
  {
    if (PaletteSet != null)
    {
      FocusPalette();
      return;
    }

    PaletteSet = new PaletteSet("Speckle (Beta) for Autocad", s_id)
    {
      Size = new Size(400, 500),
      DockEnabled = (DockSides)((int)DockSides.Left + (int)DockSides.Right)
    };

    var builder = SpeckleContainerBuilder.CreateInstance();

    // init DI
    _disposableLogger = Connector.Initialize(AppUtils.App, AppUtils.Version);
    Container = builder
      .LoadAutofacModules(
        Assembly.GetExecutingAssembly(),
        [Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location).NotNull()]
      )
      .Build();

    var panelWebView = Container.Resolve<DUI3ControlWebView>();

    PaletteSet.AddVisual("Speckle (Beta) for Autocad WebView", panelWebView);

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
