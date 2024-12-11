using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Sdk;

namespace Speckle.HostApps;

public static class ServiceCollectionExtensions
{
  public static void AddHostAppTesting<TTestBinding>(this IServiceCollection services)
  where TTestBinding : class, IBinding
  {
    services.AddSingleton<IBinding, TTestBinding>();
   services.AddMatchingInterfacesAsTransient(typeof(TestExecutor).Assembly);
  }

  public static void UseHostAppTesting(this IServiceProvider serviceProvider)
  {
    SpeckleXunitTestFramework.ServiceProvider = serviceProvider;
  }
}
