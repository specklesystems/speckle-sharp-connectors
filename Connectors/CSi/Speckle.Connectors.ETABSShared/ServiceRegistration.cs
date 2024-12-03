using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.ETABSShared;

namespace Speckle.Connectors.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddETABS(this IServiceCollection services)
  {
    services.AddETABSConverters();

    return services;
  }
}
