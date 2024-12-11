using Microsoft.Extensions.DependencyInjection;
using Speckle.Sdk;

namespace Speckle.HostApps;

public static class ServiceCollectionExtensions
{
  public static void AddHostAppTesting(this IServiceCollection services)
  {
   services.AddMatchingInterfacesAsTransient(typeof(TestExecutor).Assembly);
  }

  public static void UseHostAppTesting(this IServiceProvider serviceProvider)
  {
    SpeckleXunitTestFramework.ServiceProvider = serviceProvider;
  }
}
