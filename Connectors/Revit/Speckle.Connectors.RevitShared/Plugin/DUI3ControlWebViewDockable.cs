#if REVIT2025
using System.Windows.Controls;
using Autodesk.Revit.UI;

namespace Speckle.Connectors.DUI.WebView;

/// <summary>
/// Wrapper that takes the <see cref="DUI3ControlWebView"/> and wraps it so that it can implement a revit specific
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
