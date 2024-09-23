using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.WebView;

public static class ContainerRegistration
{
  public static void AddDUIView(this SpeckleContainerBuilder speckleContainerBuilder)
  {
    // send operation and dependencies
    speckleContainerBuilder.AddSingleton<DUI3ControlWebView>();
    speckleContainerBuilder.AddSingleton<IBrowserScriptExecutor>(c => c.Resolve<DUI3ControlWebView>());
  }
}
