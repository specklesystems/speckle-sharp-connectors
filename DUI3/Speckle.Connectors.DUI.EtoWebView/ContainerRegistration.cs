using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.DUI.EtoWebView;

public static class ContainerRegistration
{
  public static void AddDUIEtoView(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddSingleton<DUI3EtoWebView>();
    serviceCollection.AddSingleton<IBrowserScriptExecutor>(c => c.GetRequiredService<DUI3EtoWebView>());
  }
}
