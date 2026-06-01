using System.Windows;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connectors.TSD2027;

internal sealed partial class MainWindow : Window
{
  public MainWindow(DUI3ControlWebView webView)
  {
    InitializeComponent();
    Content = webView;
  }
}
