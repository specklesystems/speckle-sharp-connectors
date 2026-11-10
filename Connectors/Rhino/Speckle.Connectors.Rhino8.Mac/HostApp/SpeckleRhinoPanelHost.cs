using System.Runtime.InteropServices;
using Eto.Forms;
using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Rhino.UI;
using Speckle.Connectors.DUI.EtoWebView;
using Speckle.Connectors.Rhino.Plugin;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Rhino panel hosting the DUI on macOS. Same panel id as the Windows WPF host so the command and docking state
/// are shared.
/// </summary>
/// <remarks>
/// Mac Rhino creates one panel per open document, but there is one DUI web view, and an Eto control has one parent.
/// Each panel takes the web view when its document's panel is shown or its document becomes active, so the visible
/// panel is never the blank one (FEA-124).
/// </remarks>
[Guid("39BC44A4-C9DC-4B0A-9A51-4C31ACBCD76A")]
public class SpeckleRhinoPanelHost : Panel, IPanel
{
  private readonly uint _documentSerialNumber;

  public SpeckleRhinoPanelHost(uint documentSerialNumber)
  {
    _documentSerialNumber = documentSerialNumber;
    RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
    TakeWebView();
  }

  public void PanelShown(uint documentSerialNumber, ShowPanelReason reason) => TakeWebView();

  public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }

  public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) =>
    RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;

  private void OnActiveDocumentChanged(object? sender, DocumentEventArgs e)
  {
    if (e.Document?.RuntimeSerialNumber == _documentSerialNumber)
    {
      TakeWebView();
    }
  }

  private void TakeWebView()
  {
    var webView = SpeckleConnectorsRhinoPlugin.Instance.Container?.GetRequiredService<DUI3EtoWebView>();
    if (webView is null || ReferenceEquals(Content, webView))
    {
      return;
    }
    if (webView.Parent is Panel previousHost)
    {
      previousHost.Content = null;
    }
    Content = webView;
  }
}
