using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connectors.MicroStation.Plugin;

/// <summary>
/// Hosts the Speckle DUI3 WebView panel in a standalone WPF window.
/// MicroStation CONNECT does not expose a DockPanePlugin API in managed code,
/// so the connector panel is presented as a regular floating window.
/// </summary>
[SuppressMessage("Design", "CA1812", Justification = "Instantiated via XAML")]
public partial class SpeckleWindow : Window
{
  private SpeckleWindow(DUI3ControlWebView webView)
  {
    InitializeComponent();
    ContentArea.Content = webView;
  }

  private static SpeckleWindow? s_instance;

  public static bool IsOpen => s_instance is { IsVisible: true };

  public static SpeckleWindow CreateAndShow(IServiceProvider container)
  {
    if (s_instance != null)
    {
      s_instance.Activate();
      return s_instance;
    }

    var webView = container.GetRequiredService<DUI3ControlWebView>();
    s_instance = new SpeckleWindow(webView);
    s_instance.Closed += (_, _) => s_instance = null;
    s_instance.Show();
    return s_instance;
  }

  public static void Show(IServiceProvider container) => CreateAndShow(container);

  public static void BringToFront() => s_instance?.Activate();
}
