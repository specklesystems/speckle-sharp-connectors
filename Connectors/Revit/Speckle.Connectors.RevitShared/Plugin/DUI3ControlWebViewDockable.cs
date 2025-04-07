#if REVIT2026
using System.Windows.Controls;
using Autodesk.Revit.UI;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connectors.Revit.Plugin;

/// <summary>
/// Wrapper that takes the <see cref="Speckle.Connectors.DUI.WebView.DUI3ControlWebView"/> and wraps it so that it can implement a revit specific
/// interface
/// </summary>
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
#endif
