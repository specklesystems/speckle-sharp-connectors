using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.DUI.Bindings;

namespace Speckle.HostApps;

public static class ServiceProviderExtensions
{
  public static T Create<T>(this IServiceProvider provider, params object[] parameters) =>
    ActivatorUtilities.CreateInstance<T>(provider, parameters);
  
  public static T GetBinding<T>(this IServiceProvider provider)
    where T : IBinding => provider.GetServices<IBinding>().OfType<T>().Single();
}
