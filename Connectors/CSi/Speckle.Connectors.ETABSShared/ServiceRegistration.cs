using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.ETABSShared.HostApp;
using Speckle.Converters.ETABSShared;

namespace Speckle.Connectors.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddETABS(this IServiceCollection services)
  {
    services.AddETABSConverters();
    services.AddScoped<CSiSendCollectionManager, ETABSSendCollectionManager>();

    return services;
  }
}
