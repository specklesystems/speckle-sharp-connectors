using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.GrasshopperShared.Registration;

public static class ServiceScopeExtensions
{
  public static T Get<T>(this IServiceScope scope) => scope.ServiceProvider.GetRequiredService<T>();
}
