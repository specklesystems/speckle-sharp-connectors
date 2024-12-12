using Microsoft.Extensions.DependencyInjection;

namespace Speckle.HostApps;

public static class ServiceProviderExtensions
{
  public static T Create<T>(this IServiceProvider provider, params object[] parameters) => ActivatorUtilities.CreateInstance<T>(provider, parameters);
}
