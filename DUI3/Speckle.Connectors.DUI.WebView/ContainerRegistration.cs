using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.WebView;

public static class ContainerRegistration
{
  /*
  public static void AddDUIView(this SpeckleContainerBuilder speckleContainerBuilder)
  {
    // send operation and dependencies
    speckleContainerBuilder.AddSingleton<DUI3ControlWebView>();
    speckleContainerBuilder.AddSingleton<IBrowserScriptExecutor>(c => c.Resolve<DUI3ControlWebView>());
  }*/

  public static void AddDUIView(this IServiceCollection serviceCollection)
  {
    // send operation and dependencies
    serviceCollection.AddSingleton<DUI3ControlWebView>();
    serviceCollection.AddSingleton<IBrowserScriptExecutor>(c => c.GetRequiredService<DUI3ControlWebView>());
  }
}
