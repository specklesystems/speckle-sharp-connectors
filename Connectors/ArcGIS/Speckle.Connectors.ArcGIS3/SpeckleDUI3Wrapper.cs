using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connectors.ArcGIS;

public class SpeckleDUI3Wrapper : UserControl
{
  public SpeckleDUI3Wrapper()
  {
    Initialize();
  }

  private void Initialize()
  {
    Content = SpeckleModule.Current.Container.GetRequiredService<DUI3ControlWebView>();
  }
}
