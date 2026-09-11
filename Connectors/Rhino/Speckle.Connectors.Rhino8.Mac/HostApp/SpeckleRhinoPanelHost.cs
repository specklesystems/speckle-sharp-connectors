using System.Runtime.InteropServices;
using Eto.Forms;
using Microsoft.Extensions.DependencyInjection;
using Rhino.UI;
using Speckle.Connectors.DUI.EtoWebView;
using Speckle.Connectors.Rhino.Plugin;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Rhino panel hosting the DUI on macOS. Same panel id as the Windows WPF host so the command and docking state
/// are shared. Mac creates one instance per document and hides/shows it on toggle (rhino-mac-connector, ticket 08).
/// </summary>
[Guid("39BC44A4-C9DC-4B0A-9A51-4C31ACBCD76A")]
public class SpeckleRhinoPanelHost : Panel
{
  public SpeckleRhinoPanelHost(uint docSn)
  {
    _ = docSn;
    Content = SpeckleConnectorsRhinoPlugin.Instance.Container?.GetRequiredService<DUI3EtoWebView>();
  }
}
