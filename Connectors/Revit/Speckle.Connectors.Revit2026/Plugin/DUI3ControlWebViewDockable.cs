using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace Speckle.Connectors.Revit2026.Plugin;

public sealed partial class DUI3ControlWebViewDockable : UserControl, Autodesk.Revit.UI.IDockablePaneProvider
{
  public DUI3ControlWebViewDockable(DUI3ControlWebView dUI3ControlWebView)
  {
    Content = dUI3ControlWebView;
  }

  public void SetupDockablePane(DockablePaneProviderData data)
  {
    data.FrameworkElement = this;
    data.InitialState = new Autodesk.Revit.UI.DockablePaneState
    {
      DockPosition = DockPosition.Tabbed,
      TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
    };
  }
}
