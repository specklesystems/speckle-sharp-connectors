using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using CefSharp;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Revit;

public partial class CefSharpPanel : Page, Autodesk.Revit.UI.IDockablePaneProvider, IBrowserScriptExecutor
{
  public CefSharpPanel()
  {
    InitializeComponent();
  }

  public void ExecuteScript(string script)
  {
    //browser can be null
    if (Browser?.IsDisposed ?? true)
    {
      return;
    }
    Browser?.Dispatcher.Invoke(
      () =>
      {
        if (Browser?.IsDisposed ?? false)
        {
          Browser.ExecuteScriptAsync(script);
        }
      },
      DispatcherPriority.Background
    );
  }

  public bool IsBrowserInitialized => Browser.IsBrowserInitialized;
  public object BrowserElement => Browser;

  public void ShowDevTools() => Browser.ShowDevTools();

  public void SetupDockablePane(Autodesk.Revit.UI.DockablePaneProviderData data)
  {
    data.FrameworkElement = this;
    data.InitialState = new Autodesk.Revit.UI.DockablePaneState
    {
      DockPosition = DockPosition.Tabbed,
      TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
    };
  }
}
