using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Rhino.UI;
using Speckle.Connectors.DUI.WebView;
using Speckle.Connectors.Rhino.Plugin;

namespace Speckle.Connectors.Rhino.HostApp;

[Guid("39BC44A4-C9DC-4B0A-9A51-4C31ACBCD76A")]
public class SpeckleRhinoPanelHost : RhinoWindows.Controls.WpfElementHost
{
  private readonly uint _docSn;
  private readonly DUI3ControlWebView? _webView;

  public SpeckleRhinoPanelHost(uint docSn)
    : base(SpeckleConnectorsRhinoPlugin.Instance.Container?.GetRequiredService<DUI3ControlWebView>(), null)
  {
    _docSn = docSn;
    _webView = SpeckleConnectorsRhinoPlugin.Instance.Container?.GetRequiredService<DUI3ControlWebView>();
    Panels.Closed += PanelsOnClosed;
  }
  
  /// <summary>
  /// This is a lot like PanelsOnClosed but called when trying to show the panel to clear out a lingering parent as PanelsOnClosed isn't called
  /// </summary>
  /// <param name="webView"></param>
  public static void Reinitialize(DUI3ControlWebView? webView)
  {
    if (webView == null)
    {
      return;
    }
    // This check comes from behavioral difference on closing Rhino Panels.
    // IsPanelVisible returns;
    //  - True, when docked Panel closed from the list on right click on panel tab,
    // whenever it is closed with this way, Rhino.Panels tries to reinit this object and expect the different UIElement, that's why we disconnect Child.
    //  - False, when detached Panel is closed by 'X' close button.
    // whenever it is closed with this way, Rhino.Panels don't create this object, that's why we do not disconnect Child UIElement.
    if (Panels.IsPanelVisible(typeof(SpeckleRhinoPanelHost).GUID))
    {
      return;
    }
    // Disconnect UIElement from WpfElementHost. Otherwise, we can't reinit panel with same DUI3ControlWebView
    if (LogicalTreeHelper.GetParent(webView) is Border border)
    {
      border.Child = null;
    }
  }
  private void PanelsOnClosed(object? sender, PanelEventArgs e)
  {
    if (e.PanelId == typeof(SpeckleRhinoPanelHost).GUID)
    {
      // This check comes from behavioral difference on closing Rhino Panels.
      // IsPanelVisible returns;
      //  - True, when docked Panel closed from the list on right click on panel tab,
      // whenever it is closed with this way, Rhino.Panels tries to reinit this object and expect the different UIElement, that's why we disconnect Child.
      //  - False, when detached Panel is closed by 'X' close button.
      // whenever it is closed with this way, Rhino.Panels don't create this object, that's why we do not disconnect Child UIElement.
      if (!Panels.IsPanelVisible(typeof(SpeckleRhinoPanelHost).GUID))
      {
        return;
      }

      // Unsubscribe from the event to prevent growing registrations.
      Panels.Closed -= PanelsOnClosed;

      // Disconnect UIElement from WpfElementHost. Otherwise, we can't reinit panel with same DUI3ControlWebView
      if (_webView != null)
      {
        // Since WpfHost inherited from Border, find the parent as border and set null it's Child.
        if (LogicalTreeHelper.GetParent(_webView) is Border border)
        {
          border.Child = null;
        }
      }
    }
  }
}
