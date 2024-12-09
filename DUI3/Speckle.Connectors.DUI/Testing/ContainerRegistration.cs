using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.DUI.Testing;

public static class ContainerRegistration
{
  public static void AddTesting(this IServiceCollection services)
  {
    services.AddTransient<TestDocumentModelStore>();
    services.AddTransient<TestBrowserBridge>();
  }
}
