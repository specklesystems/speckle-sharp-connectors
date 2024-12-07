using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.ETABSShared.HostApp;
using Speckle.Converters.ETABSShared;

namespace Speckle.Connectors.ETABSShared;

public static class ServiceRegistration
{
  public static IServiceCollection AddEtabs(this IServiceCollection services)
  {
    services.AddEtabsConverters();
    services.AddScoped<CsiSendCollectionManager, EtabsSendCollectionManager>();

    return services;
  }
}
